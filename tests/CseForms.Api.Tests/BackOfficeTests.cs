using System.Net;
using System.Text.RegularExpressions;
using CseForms.Api.Cli;
using CseForms.Application.BackOffice;
using CseForms.Domain.Forms;
using CseForms.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Tests;

public partial class BackOfficeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "mot-de-passe-fictif-2026";

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    [Theory]
    [InlineData("GET", "/admin/connexion")]
    [InlineData("POST", "/admin/connexion")]
    [InlineData("GET", "/admin")]
    public async Task Back_office_over_plain_http_is_refused_with_400(string method, string path)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("HTTPS", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(response.Headers, h => h.Key == "Set-Cookie");
    }

    [Fact]
    public async Task Public_pages_stay_available_over_http()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") });

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/public/challenge")).StatusCode);
    }

    private async Task<int> RunCliAsync(string stdin, params string[] args)
    {
        var output = new StringWriter();
        var code = await UserCommands.RunAsync(factory.Services, ["users", .. args], new StringReader(stdin), output, interactive: false);
        LastCliOutput = output.ToString();
        return code;
    }

    private string LastCliOutput { get; set; } = string.Empty;

    private async Task<string> CreateUserAsync(string role)
    {
        var email = $"{role}-{Guid.NewGuid():N}@example.test";
        Assert.Equal(0, await RunCliAsync(Password, "create", "--email", email, "--name", "Élu fictif", "--role", role));
        return email;
    }

    private static string Token(string html) => TokenPattern().Match(html).Groups[1].Value;

    private async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password, string? returnUrl = null)
    {
        var page = await client.GetStringAsync("/admin/connexion");
        var fields = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = Token(page),
        };
        if (returnUrl is not null)
        {
            fields["ReturnUrl"] = returnUrl;
        }

        return await client.PostAsync("/admin/connexion", new FormUrlEncodedContent(fields));
    }

    private async Task<(Form Form, Guid SubmissionId)> SeedSubmissionAsync(string slug, string data = """{"nom":"DUPONT","site":"Site fictif"}""")
    {
        await using var db = factory.Database.CreateContext();
        var form = await TestData.AddPublishedFormAsync(db, slug);
        var submission = form.CreateSubmission($"{slug}-0001", data, DateTimeOffset.UtcNow, 0);
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();
        return (form, submission.Id);
    }

    private async Task<List<(string Action, Guid? UserId, string? Target)>> AuditAsync(string? target = null)
    {
        await using var db = factory.Database.CreateContext();
        return (await db.AuditLog.Where(a => target == null || a.Target == target).OrderBy(a => a.OccurredAt).ToListAsync())
            .Select(a => (a.Action, a.UserId, a.Target))
            .ToList();
    }

    [Fact]
    public async Task Anonymous_visitor_is_sent_to_login()
    {
        var response = await Client().GetAsync("/admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/admin/connexion?returnUrl=%2Fadmin", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deep_link_survives_login_and_viewing_is_audited()
    {
        var admin = await CreateUserAsync(AppRoles.Admin);
        var (_, submissionId) = await SeedSubmissionAsync("bo-lien");
        var client = Client();

        var anonymous = await client.GetAsync($"/admin/reponses/{submissionId}");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);

        var login = await LoginAsync(client, admin, Password, $"/admin/reponses/{submissionId}");
        Assert.Equal($"/admin/reponses/{submissionId}", login.Headers.Location!.ToString());

        var page = await client.GetAsync($"/admin/reponses/{submissionId}");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("bo-lien-0001", html, StringComparison.Ordinal);
        Assert.Contains("DUPONT", html, StringComparison.Ordinal);
        Assert.Contains("Lieu de travail", html, StringComparison.Ordinal);

        Assert.Contains(await AuditAsync($"submission:{submissionId}"), e => e.Action == AuditActions.SubmissionViewed);
    }

    [Fact]
    public async Task Session_cookie_is_secure_httponly_and_strict()
    {
        var admin = await CreateUserAsync(AppRoles.Admin);

        var response = await LoginAsync(Client(), admin, Password);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-cse-session=", StringComparison.Ordinal)).ToLowerInvariant();

        Assert.Contains("secure", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.Ordinal);
        Assert.Contains("samesite=strict", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("expires", cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_account_get_the_same_message()
    {
        var reader = await CreateUserAsync(AppRoles.Reader);

        var wrong = await (await LoginAsync(Client(), reader, "mauvais-mot-de-passe")).Content.ReadAsStringAsync();
        var unknown = await (await LoginAsync(Client(), "inconnu@example.test", Password)).Content.ReadAsStringAsync();

        Assert.Contains("Identifiant ou mot de passe incorrect.", wrong, StringComparison.Ordinal);
        Assert.Contains("Identifiant ou mot de passe incorrect.", unknown, StringComparison.Ordinal);
        Assert.Contains(await AuditAsync(), e => e.Action == AuditActions.LoginFailed && e.UserId == null);
    }

    [Fact]
    public async Task Account_is_locked_after_five_failures()
    {
        var reader = await CreateUserAsync(AppRoles.Reader);
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(Client(), reader, "mauvais-mot-de-passe");
        }

        var response = await LoginAsync(Client(), reader, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("temporairement bloqué", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://exemple.invalid/piege")]
    [InlineData("//exemple.invalid")]
    [InlineData("/f/inscription")]
    public async Task Return_url_outside_back_office_is_ignored(string returnUrl)
    {
        var admin = await CreateUserAsync(AppRoles.Admin);

        var response = await LoginAsync(Client(), admin, Password, returnUrl);

        Assert.Equal("/admin", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Editor_without_access_gets_not_found_until_granted()
    {
        var editor = await CreateUserAsync(AppRoles.Editor);
        var (form, submissionId) = await SeedSubmissionAsync("bo-droits");
        var client = Client();
        await LoginAsync(client, editor, Password);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/reponses/{submissionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/formulaires/{form.Id}/reponses")).StatusCode);
        Assert.DoesNotContain("bo-droits", await client.GetStringAsync("/admin"), StringComparison.Ordinal);
        Assert.Contains(await AuditAsync($"submission:{submissionId}"), e => e.Action == AuditActions.AccessDenied);

        Assert.Equal(0, await RunCliAsync(string.Empty, "grant", "--email", editor, "--form", "bo-droits"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/admin/reponses/{submissionId}")).StatusCode);
        Assert.Contains("bo-droits-0001", await client.GetStringAsync($"/admin/formulaires/{form.Id}/reponses"), StringComparison.Ordinal);

        Assert.Equal(0, await RunCliAsync(string.Empty, "revoke", "--email", editor, "--form", "bo-droits"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/reponses/{submissionId}")).StatusCode);
    }

    [Fact]
    public async Task Submitted_values_are_html_encoded()
    {
        var admin = await CreateUserAsync(AppRoles.Admin);
        var (_, submissionId) = await SeedSubmissionAsync("bo-xss", """{"nom":"<script>alert(1)</script>","site":"Site fictif"}""");
        var client = Client();
        await LoginAsync(client, admin, Password);

        var html = await client.GetStringAsync($"/admin/reponses/{submissionId}");

        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_requires_post_with_antiforgery_token()
    {
        var admin = await CreateUserAsync(AppRoles.Admin);
        var client = Client();
        await LoginAsync(client, admin, Password);

        await client.GetAsync("/admin/deconnexion");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);

        var withoutToken = await client.PostAsync("/admin/deconnexion", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);

        var home = await client.GetStringAsync("/admin");
        var logout = await client.PostAsync("/admin/deconnexion", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(home) }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task Login_without_antiforgery_token_is_rejected()
    {
        var response = await Client().PostAsync("/admin/connexion", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["Email"] = "x@example.test", ["Password"] = Password }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Back_office_pages_are_never_cached_nor_indexed()
    {
        var response = await Client().GetAsync("/admin/connexion");

        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Contains("script-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_filters_by_site_and_case_number()
    {
        var admin = await CreateUserAsync(AppRoles.Admin);
        var (form, _) = await SeedSubmissionAsync("bo-liste");
        var client = Client();
        await LoginAsync(client, admin, Password);

        var match = await client.GetStringAsync($"/admin/formulaires/{form.Id}/reponses?site=Site+fictif&q=liste");
        var none = await client.GetStringAsync($"/admin/formulaires/{form.Id}/reponses?site=Autre");

        Assert.Contains("bo-liste-0001", match, StringComparison.Ordinal);
        Assert.DoesNotContain("DUPONT", match, StringComparison.Ordinal);
        Assert.Contains("0 réponse(s)", none, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("court", "admin")]
    [InlineData(Password, "superadmin")]
    public async Task Cli_refuses_weak_password_or_unknown_role(string password, string role)
    {
        Assert.Equal(1, await RunCliAsync(password, "create", "--email", $"refus-{Guid.NewGuid():N}@example.test", "--name", "X", "--role", role));
    }

    [Fact]
    public async Task Cli_reports_missing_options_and_unknown_form()
    {
        Assert.Equal(1, await RunCliAsync(string.Empty, "create", "--email", "x@example.test"));
        Assert.Contains("--name", LastCliOutput, StringComparison.Ordinal);

        var editor = await CreateUserAsync(AppRoles.Editor);
        Assert.Equal(1, await RunCliAsync(string.Empty, "grant", "--email", editor, "--form", "inexistant"));
        Assert.Equal(0, await RunCliAsync(string.Empty, "list"));
        Assert.Contains(editor, LastCliOutput, StringComparison.Ordinal);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
