using CseForms.Application.BackOffice;
using CseForms.Infrastructure.Identity;
using CseForms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Cli;

/// <summary>
/// Gestion des comptes en ligne de commande, en attendant l'écran d'administration :
/// <c>users create|grant|revoke|list</c>. Aucun compte n'est créé par défaut et aucun mot de passe
/// ne transite en argument : il est saisi au terminal (ou lu sur l'entrée standard).
/// </summary>
internal static class UserCommands
{
    public const string Usage = """
        Utilisation :
          users create --email <email> --name <nom affiché> --role <admin|editor|reader>
          users grant  --email <email> --form <slug>
          users revoke --email <email> --form <slug>
          users list
        Le mot de passe (12 caractères minimum) est demandé au terminal, ou lu sur l'entrée standard.
        """;

    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] == "users";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextReader input, TextWriter output, bool interactive)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var options = ParseOptions(args.Skip(2));

        try
        {
            return args.ElementAtOrDefault(1) switch
            {
                "create" => await CreateAsync(provider, options, input, output, interactive),
                "grant" => await GrantAsync(provider, options, output, grant: true),
                "revoke" => await GrantAsync(provider, options, output, grant: false),
                "list" => await ListAsync(provider, output),
                _ => Fail(output, Usage),
            };
        }
        catch (KeyNotFoundException ex)
        {
            return Fail(output, ex.Message);
        }
    }

    private static async Task<int> CreateAsync(
        IServiceProvider provider,
        Dictionary<string, string> options,
        TextReader input,
        TextWriter output,
        bool interactive)
    {
        var email = Required(options, "email");
        var name = Required(options, "name");
        var role = Required(options, "role");
        if (!AppRoles.All.Contains(role))
        {
            return Fail(output, "Rôle inconnu : admin, editor ou reader.");
        }

        var password = ReadPassword(input, output, interactive);
        if (password is null)
        {
            return Fail(output, "Les deux saisies du mot de passe diffèrent.");
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
            return Fail(output, string.Join(Environment.NewLine, created.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(user, role);
        await output.WriteLineAsync($"Compte créé : {user.Email} ({role}).");
        return 0;
    }

    private static async Task<int> GrantAsync(IServiceProvider provider, Dictionary<string, string> options, TextWriter output, bool grant)
    {
        var db = provider.GetRequiredService<CseFormsDbContext>();
        var email = Required(options, "email").Trim();
        var slug = Required(options, "form").Trim().ToLowerInvariant();

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
            await output.WriteLineAsync($"{user.Email} | {user.DisplayName} | {string.Join(", ", roles)} | {string.Join(", ", forms)}");
        }

        return 0;
    }

    private static string? ReadPassword(TextReader input, TextWriter output, bool interactive)
    {
        if (!interactive)
        {
            return input.ReadLine() ?? string.Empty;
        }

        var first = ReadHidden(output, "Mot de passe : ");
        var second = ReadHidden(output, "Confirmation : ");
        return first == second ? first : null;
    }

    private static string ReadHidden(TextWriter output, string prompt)
    {
        output.Write(prompt);
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                output.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        string? pending = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                pending = arg[2..];
            }
            else if (pending is not null)
            {
                options[pending] = arg;
                pending = null;
            }
        }

        return options;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new KeyNotFoundException($"Option --{name} manquante.{Environment.NewLine}{Usage}");

    private static int Fail(TextWriter output, string message)
    {
        output.WriteLine(message);
        return 1;
    }
}
