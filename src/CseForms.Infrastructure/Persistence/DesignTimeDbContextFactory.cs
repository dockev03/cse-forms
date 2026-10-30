using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CseForms.Infrastructure.Persistence;

/// <summary>
/// Utilisé par <c>dotnet ef</c>. La chaîne de connexion vient de la variable
/// d'environnement <c>ConnectionStrings__Default</c> ; aucune valeur par défaut.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CseFormsDbContext>
{
    public CseFormsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? throw new InvalidOperationException("Variable d'environnement ConnectionStrings__Default absente.");

        var options = new DbContextOptionsBuilder<CseFormsDbContext>();
        DependencyInjection.ConfigureDbContext(options, connectionString);

        return new CseFormsDbContext(options.Options);
    }
}
