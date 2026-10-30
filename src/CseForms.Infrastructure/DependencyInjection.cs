using CseForms.Application.Common;
using CseForms.Application.PublicForms;
using CseForms.Infrastructure.Persistence;
using CseForms.Infrastructure.PublicForms;
using CseForms.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CseForms.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Chaîne de connexion '{ConnectionStringName}' absente (variable ConnectionStrings__{ConnectionStringName}).");
        }

        services.AddDbContext<CseFormsDbContext>(options => ConfigureDbContext(options, connectionString));
        services.AddScoped<IPublicFormStore, PublicFormStore>();
        services.AddSingleton<IAccessCodeHasher, AccessCodeHasher>();

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
