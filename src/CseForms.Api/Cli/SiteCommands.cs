using CseForms.Domain.Common;
using CseForms.Domain.Sites;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Cli;

/// <summary>
/// Liste des sites, réutilisée par les champs de type <c>site</c> de tous les formulaires.
/// Un site n'est jamais supprimé : il est désactivé, pour que les anciennes réponses restent lisibles.
/// </summary>
internal static class SiteCommands
{
    public const string Usage = """
        Utilisation :
          sites add     --label <libellé>
          sites disable --label <libellé>
          sites enable  --label <libellé>
          sites list
        """;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CseFormsDbContext>();
        var options = CliSupport.ParseOptions(args.Skip(2));

        try
        {
            return args.ElementAtOrDefault(1) switch
            {
                "add" => await AddAsync(db, CliSupport.Required(options, Usage, "label"), output),
                "disable" => await SetActiveAsync(db, CliSupport.Required(options, Usage, "label"), active: false, output),
                "enable" => await SetActiveAsync(db, CliSupport.Required(options, Usage, "label"), active: true, output),
                "list" => await ListAsync(db, output),
                _ => CliSupport.Fail(output, Usage),
            };
        }
        catch (KeyNotFoundException ex)
        {
            return CliSupport.Fail(output, ex.Message);
        }
        catch (DomainException ex)
        {
            return CliSupport.Fail(output, ex.Message);
        }
    }

    private static async Task<int> AddAsync(CseFormsDbContext db, string label, TextWriter output)
    {
        var site = new Site(label);
        if (await db.Sites.AnyAsync(s => s.Label == site.Label))
        {
            return CliSupport.Fail(output, $"Le site « {site.Label} » existe déjà.");
        }

        db.Sites.Add(site);
        await db.SaveChangesAsync();
        await output.WriteLineAsync($"Site « {site.Label} » ajouté.");
        return 0;
    }

    private static async Task<int> SetActiveAsync(CseFormsDbContext db, string label, bool active, TextWriter output)
    {
        var trimmed = label.Trim();
        var site = await db.Sites.SingleOrDefaultAsync(s => s.Label == trimmed)
            ?? throw new KeyNotFoundException("Site introuvable.");

        if (active)
        {
            site.Activate();
        }
        else
        {
            site.Deactivate();
        }

        await db.SaveChangesAsync();
        await output.WriteLineAsync(active ? $"Site « {site.Label} » activé." : $"Site « {site.Label} » désactivé.");
        return 0;
    }

    private static async Task<int> ListAsync(CseFormsDbContext db, TextWriter output)
    {
        foreach (var site in await db.Sites.AsNoTracking().OrderBy(s => s.Label).ToListAsync())
        {
            await output.WriteLineAsync($"{site.Label} | {(site.IsActive ? "actif" : "inactif")}");
        }

        return 0;
    }
}
