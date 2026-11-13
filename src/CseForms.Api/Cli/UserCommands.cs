using CseForms.Application.BackOffice;
using CseForms.Infrastructure.Identity;
using CseForms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Cli;

/// <summary>
/// Gestion des comptes en ligne de commande, en secours de l'écran « Comptes » du back-office
/// (par exemple si plus aucun administrateur ne peut se connecter) :
/// <c>users create|grant|revoke|reset-password|reset-2fa|list</c>. Aucun mot de passe ne transite en argument :
/// il est saisi au terminal (ou lu sur l'entrée standard).
/// </summary>
internal static class UserCommands
{
    public const string Usage = """
        Utilisation :
          users create --email <email> --name <nom affiché> --role <admin|editor|reader>
          users grant  --email <email> --form <slug>
          users revoke --email <email> --form <slug>
          users reset-password --email <email>
          users reset-2fa --email <email>
          users list
        Le mot de passe (12 caractères minimum) est demandé au terminal, ou lu sur l'entrée standard.
        """;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextReader input, TextWriter output, bool interactive)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var options = CliSupport.ParseOptions(args.Skip(2));

        try
        {
            return args.ElementAtOrDefault(1) switch
            {
                "create" => await CreateAsync(provider, options, input, output, interactive),
                "grant" => await GrantAsync(provider, options, output, grant: true),
                "revoke" => await GrantAsync(provider, options, output, grant: false),
                "reset-password" => await ResetPasswordAsync(provider, options, output),
                "reset-2fa" => await ResetTwoFactorAsync(provider, options, output),
                "list" => await ListAsync(provider, output),
                _ => CliSupport.Fail(output, Usage),
            };
        }
        catch (KeyNotFoundException ex)
        {
            return CliSupport.Fail(output, ex.Message);
        }
    }

    private static async Task<int> CreateAsync(
        IServiceProvider provider,
        Dictionary<string, string> options,
        TextReader input,
        TextWriter output,
        bool interactive)
    {
        var email = CliSupport.Required(options, Usage, "email");
        var name = CliSupport.Required(options, Usage, "name");
        var role = CliSupport.Required(options, Usage, "role");
        if (!AppRoles.All.Contains(role))
        {
            return CliSupport.Fail(output, "Rôle inconnu : admin, editor ou reader.");
        }

        var password = CliSupport.ReadSecret(input, output, interactive, "Mot de passe : ");
        if (password is null)
        {
            return CliSupport.Fail(output, "Les deux saisies du mot de passe diffèrent.");
        }

        // Mot de passe choisi au terminal par l'élu lui-même : pas de changement imposé.
        var created = await provider.GetRequiredService<AccountAdministration>()
            .CreateWithPasswordAsync(email, name, role, password, mustChangePassword: false);
        if (!created.Succeeded)
        {
            return CliSupport.Fail(output, string.Join(Environment.NewLine, created.Errors));
        }

        await output.WriteLineAsync($"Compte créé : {email.Trim()} ({role}).");
        return 0;
    }

    private static async Task<int> GrantAsync(IServiceProvider provider, Dictionary<string, string> options, TextWriter output, bool grant)
    {
        var db = provider.GetRequiredService<CseFormsDbContext>();
        var email = CliSupport.Required(options, Usage, "email").Trim();
        var slug = CliSupport.Required(options, Usage, "form").Trim().ToLowerInvariant();

        var user = await provider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email)
            ?? throw new KeyNotFoundException("Compte introuvable.");
        var formId = await db.Forms.Where(f => f.Slug == slug).Select(f => (Guid?)f.Id).SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Formulaire introuvable.");

        var existing = await db.UserFormAccess.FindAsync(user.Id, formId);
        if (grant && existing is null)
        {
            db.UserFormAccess.Add(new UserFormAccess { UserId = user.Id, FormId = formId });
        }
        else if (!grant && existing is not null)
        {
            db.UserFormAccess.Remove(existing);
        }

        await db.SaveChangesAsync();
        await output.WriteLineAsync(grant
            ? $"Accès au formulaire « {slug} » accordé à {email}."
            : $"Accès au formulaire « {slug} » retiré à {email}.");
        return 0;
    }

    /// <summary>Mot de passe oublié : mot de passe provisoire affiché une fois, à changer à la connexion.</summary>
    private static async Task<int> ResetPasswordAsync(IServiceProvider provider, Dictionary<string, string> options, TextWriter output)
    {
        var user = await FindAsync(provider, options);
        var result = await provider.GetRequiredService<AccountAdministration>().ResetPasswordAsync(null, user.Id, CancellationToken.None);
        if (!result.Succeeded)
        {
            return CliSupport.Fail(output, string.Join(Environment.NewLine, result.Errors));
        }

        await output.WriteLineAsync($"Mot de passe provisoire de {user.Email} (à transmettre de vive voix, à changer à la connexion) : {result.TemporaryPassword}");
        return 0;
    }

    /// <summary>
    /// Téléphone perdu sans code de secours : désactive la double authentification et change la clé.
    /// L'élu se reconnecte avec son seul mot de passe puis la réactive ; ses appareils de confiance sont oubliés.
    /// </summary>
    private static async Task<int> ResetTwoFactorAsync(IServiceProvider provider, Dictionary<string, string> options, TextWriter output)
    {
        var user = await FindAsync(provider, options);
        await provider.GetRequiredService<AccountAdministration>().ResetTwoFactorAsync(null, user.Id, CancellationToken.None);
        await output.WriteLineAsync($"Double authentification réinitialisée pour {user.Email} : connexion par mot de passe seul jusqu'à sa réactivation.");
        return 0;
    }

    private static async Task<AppUser> FindAsync(IServiceProvider provider, Dictionary<string, string> options)
    {
        var email = CliSupport.Required(options, Usage, "email").Trim();
        return await provider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email)
            ?? throw new KeyNotFoundException("Compte introuvable.");
    }

    private static async Task<int> ListAsync(IServiceProvider provider, TextWriter output)
    {
        var db = provider.GetRequiredService<CseFormsDbContext>();
        var users = provider.GetRequiredService<UserManager<AppUser>>();

        foreach (var user in await db.Users.OrderBy(u => u.Email).ToListAsync())
        {
            var roles = await users.GetRolesAsync(user);
            var forms = await db.UserFormAccess
                .Where(a => a.UserId == user.Id)
                .Join(db.Forms, a => a.FormId, f => f.Id, (_, f) => f.Slug)
                .OrderBy(s => s)
                .ToListAsync();
            var twoFactor = user.TwoFactorEnabled ? "2FA" : "sans 2FA";
            var state = user.DisabledAt is not null ? "désactivé" : user.MustChangePassword ? "mot de passe provisoire" : "actif";
            await output.WriteLineAsync($"{user.Email} | {user.DisplayName} | {string.Join(", ", roles)} | {twoFactor} | {state} | {string.Join(", ", forms)}");
        }

        return 0;
    }
}
