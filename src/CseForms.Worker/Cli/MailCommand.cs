using CseForms.Application.Notifications;
using CseForms.Application.Settings;

namespace CseForms.Worker.Cli;

/// <summary>
/// Test des réglages SMTP : <c>dotnet CseForms.Worker.dll mail test --to &lt;adresse&gt;</c>.
/// Lancé depuis le Worker, seul à détenir les identifiants SMTP, avec exactement sa configuration.
/// </summary>
internal static class MailCommand
{
    public const string Usage = """
        Utilisation :
          mail test --to <adresse>   envoie un email de test avec les réglages SMTP actuels
        """;

    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] == "mail";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output, TimeProvider timeProvider)
    {
        if (args.ElementAtOrDefault(1) != "test" || args.Length != 4 || args[2] != "--to")
        {
            await output.WriteLineAsync(Usage);
            return 1;
        }

        var to = args[3].Trim();
        if (!SettingKeys.IsPlainEmail(to))
        {
            await output.WriteLineAsync("Adresse email invalide.");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var settings = await scope.ServiceProvider.GetRequiredService<ISettingReader>().GetAllAsync(CancellationToken.None);
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        try
        {
            await sender.SendAsync(TestEmail.Build(to, settings, timeProvider.GetUtcNow()), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Affiché à l'administrateur qui lance la commande, jamais journalisé : le détail aide au diagnostic.
            await output.WriteLineAsync($"Échec de l'envoi ({ex.GetType().Name}) : {ex.Message}");
            return 1;
        }

        await output.WriteLineAsync("Email de test envoyé. Vérifiez sa réception, et le dossier des indésirables.");
        return 0;
    }
}
