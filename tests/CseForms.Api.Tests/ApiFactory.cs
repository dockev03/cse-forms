using CseForms.Application.Pdf;
using CseForms.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CseForms.Api.Tests;

/// <summary>API complète sur une base PostgreSQL jetable.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public PostgresFixture Database { get; } = new();

    /// <summary>Dossier de stockage jetable, supprimé avec la fabrique.</summary>
    public string StoragePath { get; } = Directory.CreateTempSubdirectory("cse-forms-api-").FullName;

    protected virtual int SubmitPermitLimit => 1000;

    /// <summary>Conversion PDF factice : garde le HTML reçu (flyer) au lieu d'appeler Gotenberg.</summary>
    public RecordingPdfConverter PdfConverter { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", Database.ConnectionString);
        builder.UseSetting("RateLimiting:PublicSubmit:PermitLimit", SubmitPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:PublicRead:PermitLimit", "1000");
        builder.UseSetting("RateLimiting:Login:PermitLimit", "1000");
        builder.UseSetting("Storage:Path", StoragePath);
        builder.UseSetting("Storage:EncryptionKey", TestData.StorageKey);
        builder.UseSetting("Gotenberg:Url", "http://127.0.0.1:9");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPdfConverter>();
            services.AddSingleton<IPdfConverter>(PdfConverter);
        });
    }

    public Task InitializeAsync() => Database.InitializeAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Database.DisposeAsync();
        Directory.Delete(StoragePath, recursive: true);
    }
}

public sealed class RecordingPdfConverter : IPdfConverter
{
    private readonly List<PdfHtml> _documents = [];

    public IReadOnlyList<PdfHtml> Documents
    {
        get
        {
            lock (_documents)
            {
                return [.. _documents];
            }
        }
    }

    public Task<byte[]> ConvertAsync(PdfHtml html, CancellationToken cancellationToken)
    {
        lock (_documents)
        {
            _documents.Add(html);
        }

        return Task.FromResult("%PDF-1.7 flyer fictif"u8.ToArray());
    }
}

public sealed class StrictRateLimitApiFactory : ApiFactory
{
    protected override int SubmitPermitLimit => 2;
}
