using CseForms.Application.BackOffice;
using CseForms.Infrastructure.Identity;
using CseForms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Cli;

/// <summary>
/// Gestion des comptes en ligne de commande, en attendant l'écran d'administration :
/// <c>users create|grant|revoke|reset-2fa|list</c>. Aucun compte n'est créé par défaut et aucun mot de passe
/// ne transite en argument : il est saisi au terminal (ou lu sur l'entrée standard).
/// </summary>
internal static class UserCommands
{
    public const string Usage = """
        Utilisation :
          users create --email <email> --name <nom affiché> --role <admin|editor|reader>
          users grant  --email <email> --form <slug>
          users revoke --email <email> --form <slug>
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

        var roles = provider.GetRequiredService<RoleManager<AppRole>>();
        foreach (var roleName in AppRoles.All)
        {
            if (!await roles.RoleExistsAsync(roleName))
            {
                await roles.CreateAsync(new AppRole { Name = roleName });
            }
        }

        var users = provider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            Email = email.Trim(),
            UserName = email.Trim(),
            EmailConfirmed = true,
            DisplayName = name.Trim(),
            CreatedAt = provider.GetRequiredService<TimeProvider>().GetUtcNow(),
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            return CliSupport.Fail(output, string.Join(Environment.NewLine, created.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(user, role);
        await output.WriteLineAsync($"Compte créé : {user.Email} ({role}).");
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

    /// <summary>
    /// Téléphone perdu sans code de secours : désactive la double authentification et change la clé.
    /// L'élu se reconnecte avec son seul mot de passe puis la réactive ; ses appareils de confiance sont oubliés.
    /// </summary>
    private static async Task<int> ResetTwoFactorAsync(IServiceProvider provider, Dictionary<string, string> options, TextWriter output)
    {
        var email = CliSupport.Required(options, Usage, "email").Trim();
        var users = provider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email) ?? throw new KeyNotFoundException("Compte introuvable.");

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await provider.GetRequiredService<IAuditLog>()
            .RecordAsync(AuditActions.TwoFactorReset, null, $"user:{user.Id}", CancellationToken.None);
        await output.WriteLineAsync($"Double authentification réinitialisée pour {email} : connexion par mot de passe seul jusqu'à sa réactivation.");
        return 0;
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
            await output.WriteLineAsync($"{user.Email} | {user.DisplayName} | {string.Join(", ", roles)} | {twoFactor} | {string.Join(", ", forms)}");
        }

        return 0;
    }
}
