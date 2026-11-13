using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CseForms.Testing;

namespace CseForms.Api.Tests;

/// <summary>Anti-robot du formulaire public : défi, preuve obligatoire, usage unique.</summary>
public class ProofOfWorkApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Url = "/api/public/forms/api-preuve/submissions";

    private async Task PublishAsync()
    {
        await using var db = factory.Database.CreateContext();
        await TestData.EnsureSiteAsync(db, "Site fictif");
        if (!db.Forms.Any(f => f.Slug == "api-preuve"))
        {
            await TestData.AddPublishedFormAsync(db, "api-preuve");
        }
    }

    private static object Body(JsonObject? proof = null) =>
        new { version = 1, data = new { nom = "fictif", site = "Site fictif" }, proof };

    private async Task<int> CountAsync()
    {
        await using var db = factory.Database.CreateContext();
        return db.Submissions.Count();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())?["code"]?.GetValue<string>();

    [Fact]
    public async Task Challenge_is_served_without_cache()
    {
        var response = await factory.CreateClient().GetAsync("/api/public/challenge");
        var challenge = (await response.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("SHA-256", challenge["algorithm"]!.GetValue<string>());
        Assert.Equal(1000, challenge["maxNumber"]!.GetValue<int>());
        Assert.Contains("?expires=", challenge["salt"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submission_without_proof_is_refused_and_not_recorded()
    {
        await PublishAsync();
        var before = await CountAsync();

        var response = await factory.CreateClient().PostAsJsonAsync(Url, Body());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("proofInvalid", await CodeAsync(response));
        Assert.Equal(before, await CountAsync());
    }

    [Fact]
    public async Task Proof_cannot_be_replayed()
    {
        await PublishAsync();
        var client = factory.CreateClient();
        var proof = await ProofOfWorkClient.SolveAsync(client);

        var first = await client.PostAsJsonAsync(Url, Body(proof));
        var replay = await client.PostAsJsonAsync(Url, Body(proof.DeepClone().AsObject()));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Equal("proofInvalid", await CodeAsync(replay));
    }

    [Fact]
    public async Task Wrong_number_is_refused()
    {
        await PublishAsync();
        var client = factory.CreateClient();
        var proof = await ProofOfWorkClient.SolveAsync(client);
        proof["number"] = (proof["number"]!.GetValue<int>() + 1) % 1001;

        var response = await client.PostAsJsonAsync(Url, Body(proof));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
