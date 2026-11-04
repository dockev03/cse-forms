using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.FormAdministration;
using CseForms.Infrastructure.BackOffice;
using CseForms.Infrastructure.FormAdministration;
using CseForms.Infrastructure.Jobs;
using CseForms.Infrastructure.Pdf;
using CseForms.Infrastructure.Storage;
using CseForms.Application.Pdf;
using Microsoft.Extensions.Options;
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
        services.AddScoped<IBackOfficeStore, BackOfficeStore>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IFormAdminStore, FormAdminStore>();
        services.AddScoped<ISubmissionPdfStore, SubmissionPdfStore>();
        services.AddScoped<PendingJobDispatcher>();

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.Section))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Path), "Storage__Path est obligatoire.")
            .Validate(o => StorageOptions.IsValidKey(o.EncryptionKey), "Storage__EncryptionKey doit contenir 32 octets encodés en base64.")
            .ValidateOnStart();
        services.AddSingleton<IFileVault, EncryptedFileVault>();

        return services;
    }

    /// <summary>Conversion HTML vers PDF par Gotenberg : uniquement dans le Worker.</summary>
    public static IServiceCollection AddPdfConversion(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GotenbergOptions>()
            .Bind(configuration.GetSection(GotenbergOptions.Section))
            .Validate(o => Uri.TryCreate(o.Url, UriKind.Absolute, out _), "Gotenberg__Url doit être une adresse absolue.")
            .Validate(o => o.TimeoutSeconds is > 0 and <= 600, "Gotenberg__TimeoutSeconds doit être compris entre 1 et 600.")
            .ValidateOnStart();

        services.AddHttpClient<IPdfConverter, GotenbergPdfConverter>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<GotenbergOptions>>().Value;
            http.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
