using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CseForms.Api.Tests;

public class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    static HealthTests()
    {
        // Base injoignable volontairement : la vivacité ne doit pas en dépendre.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=1");
    }

    [Fact]
    public async Task Live_endpoint_does_not_depend_on_database()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_endpoint_reports_unavailable_database_without_details()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body);
    }
}
