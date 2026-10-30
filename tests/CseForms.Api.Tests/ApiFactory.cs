using CseForms.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CseForms.Api.Tests;

/// <summary>API complète sur une base PostgreSQL jetable.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public PostgresFixture Database { get; } = new();

    protected virtual int SubmitPermitLimit => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", Database.ConnectionString);
        builder.UseSetting("RateLimiting:PublicSubmit:PermitLimit", SubmitPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:PublicRead:PermitLimit", "1000");
        builder.UseSetting("RateLimiting:Login:PermitLimit", "1000");
    }

    public Task InitializeAsync() => Database.InitializeAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Database.DisposeAsync();
    }
}

public sealed class StrictRateLimitApiFactory : ApiFactory
{
    protected override int SubmitPermitLimit => 2;
}
