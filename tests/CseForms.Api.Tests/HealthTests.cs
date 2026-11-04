using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CseForms.Api.Tests;

public class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    // Base injoignable volontairement : la vivacité ne doit pas en dépendre.
    private HttpClient CreateClient() => factory
        .WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=1")
            .UseSetting("Storage:Path", Path.GetTempPath())
            .UseSetting("Storage:EncryptionKey", CseForms.Testing.TestData.StorageKey))
        .CreateClient();

    [Fact]
    public async Task Live_endpoint_does_not_depend_on_database()
    {
        var response = await CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_endpoint_reports_unavailable_database_without_details()
    {
        var response = await CreateClient().GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body);
    }
}
