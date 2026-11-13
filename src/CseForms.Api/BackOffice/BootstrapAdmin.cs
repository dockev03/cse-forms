using CseForms.Infrastructure.Identity;

namespace CseForms.Api.BackOffice;

/// <summary>
/// Administrateur initial, lu dans la configuration (<c>BootstrapAdmin__Email</c>, <c>BootstrapAdmin__Password</c>,
/// <c>BootstrapAdmin__Name</c> facultatif). Créé au démarrage uniquement si la base ne contient aucun compte ;
/// ignoré ensuite. Son mot de passe est provisoire : il doit être changé à la première connexion.
/// </summary>
internal static class BootstrapAdmin
{
    public const string Section = "BootstrapAdmin";

    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)
    {
        var section = configuration.GetSection(Section);
        var email = section["Email"];
        var password = section["Password"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrEmpty(password))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Administrateur initial : BootstrapAdmin__Email et BootstrapAdmin__Password vont ensemble.");
        }

        await using var scope = services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdministration>();
        if (await accounts.BootstrapAdminAsync(email, section["Name"], password, cancellationToken))
        {
            // Ni l'adresse ni le mot de passe dans les journaux.
            logger.LogWarning("Administrateur initial créé. Mot de passe à changer à la première connexion ; retirer ensuite BootstrapAdmin__Password du fichier .env.");
        }
    }
}
