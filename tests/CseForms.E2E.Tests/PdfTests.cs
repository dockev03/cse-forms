using CseForms.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace CseForms.E2E.Tests;

/// <summary>PDF de bout en bout : soumission, tâche du Worker, conversion Gotenberg, téléchargement authentifié.</summary>
public sealed class PdfTests(E2EEnvironment app) : E2ETestBase(app)
{
    [E2EFact]
    public Task Pdf_is_generated_in_background_then_downloaded_by_an_editor() => RunAsync(Desktop, async page =>
    {
        var caseNumber = await FictiveSubmissions.SubmitCdiAsync(App, nom: "Pdf");
        await LoginAsync(page, App.Admin);
        var detailUrl = await BackOfficeTests.OpenSubmissionAsync(page, caseNumber);

        // La fiche indique la génération en cours puis le PDF disponible : on recharge comme le ferait l'élu.
        var download = page.GetByRole(AriaRole.Link, new() { Name = "Télécharger le PDF" });
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!await download.IsVisibleAsync())
        {
            Assert.True(DateTime.UtcNow < deadline, "PDF toujours indisponible après 60 secondes.");
            await Expect(page.GetByText("La dernière génération a échoué")).ToHaveCountAsync(0);
            await page.WaitForTimeoutAsync(1000);
            await page.GotoAsync(detailUrl);
        }

        var href = await download.GetAttributeAsync("href") ?? throw new InvalidOperationException("Lien du PDF sans adresse.");
        var response = await page.APIRequest.GetAsync(href);
        Assert.Equal(200, response.Status);
        Assert.Equal("application/pdf", response.Headers["content-type"]);
        Assert.Contains("no-store", response.Headers["cache-control"]);
        var body = await response.BodyAsync();
        Assert.Equal("%PDF-"u8.ToArray(), body[..5]);

        // Sans session, le même lien renvoie vers la connexion et jamais le fichier.
        await using var anonymous = await App.Playwright.APIRequest.NewContextAsync(new() { BaseURL = App.BaseUrl, MaxRedirects = 0, IgnoreHTTPSErrors = true });
        var refused = await anonymous.GetAsync(href);
        Assert.NotEqual(200, refused.Status);
        Assert.NotEqual("application/pdf", refused.Headers.GetValueOrDefault("content-type"));
    });
}
