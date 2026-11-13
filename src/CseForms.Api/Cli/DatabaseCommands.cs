using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Cli;

/// <summary>
/// Mise à jour du schéma de la base, sans SDK .NET : lancée une fois avant le démarrage de l'API
/// (service <c>migrate</c> de <c>docker-compose.yml</c>). Sans effet si la base est déjà à jour.
/// </summary>
internal static class DatabaseCommands
{
    public const string Usage = """
        Utilisation :
          db migrate
        """;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output)
    {
        if (args.ElementAtOrDefault(1) != "migrate")
        {
            return CliSupport.Fail(output, Usage);
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CseFormsDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            await output.WriteLineAsync("Base à jour, aucune migration à appliquer.");
            return 0;
        }

        await db.Database.MigrateAsync();
        await output.WriteLineAsync($"{pending.Count} migration(s) appliquée(s) : {string.Join(", ", pending)}.");
        return 0;
    }
}
