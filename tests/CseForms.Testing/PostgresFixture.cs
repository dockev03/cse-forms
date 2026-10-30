using CseForms.Infrastructure;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CseForms.Testing;

/// <summary>
/// Crée une base PostgreSQL jetable, applique les migrations, puis la supprime.
/// Le serveur est donné par la variable d'environnement <c>CSEFORMS_TEST_POSTGRES</c>
/// (chaîne de connexion d'un rôle autorisé à créer des bases).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "CSEFORMS_TEST_POSTGRES";

    private readonly string _serverConnectionString;

    public PostgresFixture()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable)
            ?? throw new InvalidOperationException($"Variable d'environnement {EnvironmentVariable} absente.");

        ConnectionString = new NpgsqlConnectionStringBuilder(_serverConnectionString)
        {
            Database = $"cseforms_test_{Guid.NewGuid():N}",
        }.ConnectionString;
    }

    public string ConnectionString { get; }

    public CseFormsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CseFormsDbContext>();
        DependencyInjection.ConfigureDbContext(options, ConnectionString);
        return new CseFormsDbContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
