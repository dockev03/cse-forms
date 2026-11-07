using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CseForms.Domain.Forms;
using CseForms.Domain.Jobs;
using CseForms.Testing;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Tests;

public class PublicFormApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Site = "Site fictif";

    private async Task<Form> PublishAsync(string slug, Action<Form>? configure = null)
    {
        await using var db = factory.Database.CreateContext();
        await TestData.EnsureSiteAsync(db, Site);
        return await TestData.AddPublishedFormAsync(db, slug, configure: configure);
    }

    private static object ValidData => new { nom = " dupont ", site = Site };

    private Task<HttpResponseMessage> SubmitAsync(string slug, object body) =>
        factory.CreateClient().PostAsJsonAsync($"/api/public/forms/{slug}/submissions", body);

    private static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    [Fact]
    public async Task Unknown_or_draft_form_returns_not_found()
    {
        await using (var db = factory.Database.CreateContext())
        {
            db.Forms.Add(new Form("api-brouillon", "Brouillon", TestData.Past));
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/public/forms/api-inconnu")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/public/forms/api-brouillon")).StatusCode);
    }

    [Fact]
    public async Task Published_form_exposes_definition_but_never_access_code_hash()
    {
        await PublishAsync("api-lecture", f => f.SetAccessCodeHash(TestData.HashAccessCode("1234")));

        var response = await factory.CreateClient().GetAsync("/api/public/forms/api-lecture");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json["version"]!.GetValue<int>());
        Assert.True(json["accepting"]!.GetValue<bool>());
        Assert.True(json["requiresAccessCode"]!.GetValue<bool>());
        Assert.Equal("nom", json["definition"]!["sections"]![0]!["fields"]![0]!["id"]!.GetValue<string>());
        Assert.DoesNotContain("AQAAAA", body, StringComparison.Ordinal);
        Assert.DoesNotContain("accessCodeHash", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Closed_form_is_visible_without_definition()
    {
        await PublishAsync("api-ferme-lecture", f => f.Close());

        var json = await ReadJsonAsync(await factory.CreateClient().GetAsync("/api/public/forms/api-ferme-lecture"));

        Assert.False(json["accepting"]!.GetValue<bool>());
        Assert.Null(json["definition"]);
    }

    [Fact]
    public async Task Valid_submission_is_stored_normalized_and_returns_case_number()
    {
        var form = await PublishAsync("api-soumission");

        var response = await SubmitAsync("api-soumission", new { version = 1, data = ValidData });
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var caseNumber = json["caseNumber"]!.GetValue<string>();
        Assert.Matches("^TST-[0-9]{4}-[0-9]{4}$", caseNumber);

        await using var db = factory.Database.CreateContext();
        var stored = await db.Submissions.SingleAsync(s => s.CaseNumber == caseNumber);
        Assert.Equal(form.CurrentVersionId, stored.FormVersionId);
        Assert.Equal("DUPONT", JsonNode.Parse(stored.DataJson)!["nom"]!.GetValue<string>());

        // PDF et notification sont demandés dans la même transaction que la réponse ; pas d'accusé sans « receipt ».
        var jobs = await db.Jobs.Where(j => j.SubmissionId == stored.Id).ToListAsync();
        Assert.Equal([JobType.Pdf, JobType.Notification], jobs.Select(j => j.Type).Order());
        Assert.All(jobs, j => Assert.Equal(JobStatus.Pending, j.Status));
    }

    [Theory]
    [InlineData("camille.fictif@example.test", true)]
    [InlineData(null, false)]
    public async Task Receipt_is_requested_only_when_the_respondent_gave_an_email(string? email, bool expected)
    {
        var slug = $"api-accuse-{expected.ToString().ToLowerInvariant()}";
        await using (var db = factory.Database.CreateContext())
        {
            await TestData.AddPublishedFormAsync(db, slug, """
                {"receipt":{"emailField":"mail"},"sections":[{"id":"s","fields":[
                  {"id":"nom","type":"text","label":"Nom","required":true},
                  {"id":"mail","type":"email","label":"Email"}]}]}
                """);
        }

        var response = await SubmitAsync(slug, new { version = 1, data = new { nom = "Fictif", mail = email } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var caseNumber = (await ReadJsonAsync(response))["caseNumber"]!.GetValue<string>();

        await using var check = factory.Database.CreateContext();
        var submission = await check.Submissions.SingleAsync(s => s.CaseNumber == caseNumber);
        Assert.Equal(expected, await check.Jobs.AnyAsync(j => j.SubmissionId == submission.Id && j.Type == JobType.Receipt));
    }

    [Fact]
    public async Task Invalid_submission_returns_field_errors_without_values()
    {
        await PublishAsync("api-invalide");

        var response = await SubmitAsync("api-invalide", new { version = 1, data = new { nom = "", site = "valeur-secrete" } });
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)!;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("invalid", json["code"]!.GetValue<string>());
        Assert.Equal(
            ["nom:required", "site:invalidOption"],
            json["errors"]!.AsArray().Select(e => $"{e!["path"]}:{e["code"]}").Order(StringComparer.Ordinal));
        Assert.DoesNotContain("valeur-secrete", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Access_code_is_required_and_checked()
    {
        await PublishAsync("api-code", f => f.SetAccessCodeHash(TestData.HashAccessCode("4821")));

        var missing = await SubmitAsync("api-code", new { version = 1, data = ValidData });
        var wrong = await SubmitAsync("api-code", new { version = 1, accessCode = "0000", data = ValidData });
        var right = await SubmitAsync("api-code", new { version = 1, accessCode = " 4821 ", data = ValidData });

        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal("accessDenied", (await ReadJsonAsync(wrong))["code"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Created, right.StatusCode);
    }

    [Fact]
    public async Task Outdated_version_is_refused_with_conflict()
    {
        await PublishAsync("api-version", f => f.Publish(TestData.SimpleDefinition, null, null, TestData.Past, "test"));

        var response = await SubmitAsync("api-version", new { version = 1, data = ValidData });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("versionChanged", (await ReadJsonAsync(response))["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Closed_or_full_form_refuses_submission()
    {
        await PublishAsync("api-ferme", f => f.Close());
        await PublishAsync("api-complet", f => f.SetMaxResponses(1));

        var closed = await SubmitAsync("api-ferme", new { version = 1, data = ValidData });
        var first = await SubmitAsync("api-complet", new { version = 1, data = ValidData });
        var full = await SubmitAsync("api-complet", new { version = 1, data = ValidData });

        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Equal("notAccepting", (await ReadJsonAsync(full))["code"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("""{"version":1,"data":[]}""")]
    [InlineData("""{"version":0,"data":{}}""")]
    [InlineData("""{"data":{}}""")]
    public async Task Malformed_request_returns_generic_bad_request(string body)
    {
        var response = await factory.CreateClient().PostAsync(
            "/api/public/forms/api-lecture/submissions",
            new StringContent(body, Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("badRequest", JsonNode.Parse(text)!["code"]!.GetValue<string>());
        Assert.DoesNotContain("Exception", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_request_is_rejected()
    {
        await PublishAsync("api-volumineux");

        var payload = JsonSerializer.Serialize(new { version = 1, data = new { nom = new string('x', 70_000), site = Site } });
        var response = await factory.CreateClient().PostAsync(
            "/api/public/forms/api-volumineux/submissions",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Security_headers_are_present()
    {
        var response = await factory.CreateClient().GetAsync("/api/public/forms/api-inconnu-entetes");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }
}

public class RateLimitTests(StrictRateLimitApiFactory factory) : IClassFixture<StrictRateLimitApiFactory>
{
    [Fact]
    public async Task Submissions_beyond_limit_are_rejected()
    {
        var client = factory.CreateClient();
        HttpResponseMessage? last = null;

        for (var i = 0; i < 3; i++)
        {
            last = await client.PostAsJsonAsync("/api/public/forms/inconnu/submissions", new { version = 1, data = new { } });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.Equal("tooManyRequests", JsonNode.Parse(await last.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
    }
}
