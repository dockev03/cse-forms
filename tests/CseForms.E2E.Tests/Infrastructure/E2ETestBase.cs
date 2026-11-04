using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using Xunit.Sdk;

namespace CseForms.E2E.Tests.Infrastructure;

/// <summary>
/// Socle des scénarios : un contexte de navigateur neuf par test (aucun cookie partagé),
/// trace Playwright et capture d'écran conservées seulement en cas d'échec.
/// </summary>
[Collection(E2ECollection.Name)]
public abstract class E2ETestBase(E2EEnvironment app)
{
    /// <summary>Dossier des traces d'échec (par défaut TestResults/playwright à la racine).</summary>
    public const string ArtifactsVariable = "CSEFORMS_E2E_ARTIFACTS";

    protected E2EEnvironment App { get; } = app;

    // IgnoreHTTPSErrors : l'application sert un certificat auto-signé jetable, accepté sans l'installer sur le système.

    /// <summary>Smartphone : le formulaire public doit se remplir sur mobile.</summary>
    protected BrowserNewContextOptions Mobile => new(App.Playwright.Devices["Pixel 7"]) { BaseURL = App.BaseUrl, Locale = "fr-FR", IgnoreHTTPSErrors = true };

    protected BrowserNewContextOptions Desktop => new() { BaseURL = App.BaseUrl, Locale = "fr-FR", IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1280, Height = 900 } };

    /// <summary>Exécute un scénario dans un contexte neuf ; en cas d'échec, garde trace, capture et journaux serveur.</summary>
    protected async Task RunAsync(BrowserNewContextOptions options, Func<IPage, Task> scenario, [CallerMemberName] string name = "")
    {
        await using var context = await App.Browser.NewContextAsync(options);
        context.SetDefaultTimeout(10_000);
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true });
        var page = await context.NewPageAsync();

        try
        {
            await scenario(page);
            await context.Tracing.StopAsync();
        }
        catch (Exception exception)
        {
            var directory = Environment.GetEnvironmentVariable(ArtifactsVariable)
                ?? Path.Combine(AppProcess.SolutionRoot, "TestResults", "playwright");
            Directory.CreateDirectory(directory);
            var prefix = Path.Combine(directory, $"{GetType().Name}.{name}");
            await page.ScreenshotAsync(new() { Path = $"{prefix}.png", FullPage = true });
            await context.Tracing.StopAsync(new() { Path = $"{prefix}.zip" });
            await File.WriteAllTextAsync($"{prefix}.log", App.ServerOutput);
            throw new XunitException($"{exception.Message}{Environment.NewLine}Trace : {prefix}.zip", exception);
        }
    }

    /// <summary>
    /// Relève les violations de CSP, les erreurs JavaScript et les réponses en erreur de la page.
    /// La demande automatique de /favicon.ico par le navigateur est ignorée.
    /// </summary>
    protected static List<string> WatchBrowserErrors(IPage page)
    {
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && !message.Text.StartsWith("Failed to load resource", StringComparison.Ordinal))
            {
                errors.Add(message.Text);
            }
        };
        page.PageError += (_, error) => errors.Add(error);
        page.Response += (_, response) =>
        {
            if (response.Status >= 400 && new Uri(response.Url).AbsolutePath != "/favicon.ico")
            {
                errors.Add($"{response.Status} {response.Url}");
            }
        };
        return errors;
    }

    protected async Task LoginAsync(IPage page, Account account)
    {
        await page.GotoAsync("/admin/connexion");
        await SubmitLoginAsync(page, account.Email, account.Password);
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Se déconnecter" })).ToBeVisibleAsync();
    }

    protected static async Task SubmitLoginAsync(IPage page, string email, string password)
    {
        await page.Locator("#Email").FillAsync(email);
        await page.Locator("#Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Se connecter" }).ClickAsync();
    }
}
