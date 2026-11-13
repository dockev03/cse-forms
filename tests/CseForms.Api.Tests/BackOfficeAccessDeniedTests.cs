using System.Net;
using CseForms.Application.BackOffice;

namespace CseForms.Api.Tests;

/// <summary>
/// Refus pour rôle insuffisant (droit sur le formulaire, mais pas sur l'action) : une page 403 dédiée,
/// sans repasser par l'écran de connexion qui renverrait vers la page refusée (boucle de redirections).
/// </summary>
public partial class BackOfficeTests
{
    private const int MaxRedirects = 5;
    private const string BackOfficeAccessDenied = "/admin/acces-refuse";

    /// <summary>Suit les redirections à la main et renvoie la réponse finale avec les adresses traversées.</summary>
    private static async Task<(HttpResponseMessage Final, List<string> Hops)> FollowAsync(HttpClient client, HttpRequestMessage request)
    {
        var hops = new List<string>();
        var response = await client.SendAsync(request);
        while (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther)
        {
            var location = response.Headers.Location!.OriginalString;
            Assert.DoesNotContain(location, hops);
            hops.Add(location);
            Assert.True(hops.Count <= MaxRedirects, $"Trop de redirections : {string.Join(" -> ", hops)}");
            response = await client.GetAsync(location);
        }

        return (response, hops);
    }

    private static async Task AssertAccessDeniedPageAsync(HttpResponseMessage final, List<string> hops)
    {
        Assert.Equal(HttpStatusCode.Forbidden, final.StatusCode);
        Assert.DoesNotContain(hops, h => h.Contains("/admin/connexion", StringComparison.Ordinal));
        var html = await final.Content.ReadAsStringAsync();
        Assert.Contains("Accès refusé", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/admin\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editor_opening_deletion_gets_access_denied_page_without_redirect_loop_and_refusal_is_audited()
    {
        var (_, submissionId) = await SeedSubmissionAsync("bo-refus-suppr");
        var client = await LoggedInAsync(AppRoles.Editor, "bo-refus-suppr");

        var (final, hops) = await FollowAsync(client, new HttpRequestMessage(HttpMethod.Get, $"/admin/reponses/{submissionId}/suppression"));

        await AssertAccessDeniedPageAsync(final, hops);
        Assert.Single(await AuditAsync($"submission:{submissionId}"), e => e.Action == AuditActions.AccessDenied);
    }

    [Fact]
    public async Task Editor_posting_deletion_gets_access_denied_page_and_refusal_is_audited()
    {
        var (_, submissionId) = await SeedSubmissionAsync("bo-refus-suppr-post");
        var client = await LoggedInAsync(AppRoles.Editor, "bo-refus-suppr-post");
        var page = await client.GetStringAsync($"/admin/reponses/{submissionId}");

        var (final, hops) = await FollowAsync(client, new HttpRequestMessage(HttpMethod.Post, $"/admin/reponses/{submissionId}/suppression")
        {
            Content = Post(page, ("confirmation", "bo-refus-suppr-post-0001")),
        });

        await AssertAccessDeniedPageAsync(final, hops);
        Assert.Contains(await AuditAsync($"submission:{submissionId}"), e => e.Action == AuditActions.AccessDenied);
    }

    [Fact]
    public async Task Reader_changing_status_gets_access_denied_page()
    {
        var (_, submissionId) = await SeedSubmissionAsync("bo-refus-statut");
        var client = await LoggedInAsync(AppRoles.Reader, "bo-refus-statut");
        var page = await client.GetStringAsync($"/admin/reponses/{submissionId}");

        var (final, hops) = await FollowAsync(client, new HttpRequestMessage(HttpMethod.Post, $"/admin/reponses/{submissionId}?handler=Status")
        {
            Content = Post(page, ("status", "entered")),
        });

        await AssertAccessDeniedPageAsync(final, hops);
    }

    [Fact]
    public async Task Reader_regenerating_pdf_gets_access_denied_page()
    {
        var (_, submissionId) = await SeedSubmissionAsync("bo-refus-pdf");
        var client = await LoggedInAsync(AppRoles.Reader, "bo-refus-pdf");
        var page = await client.GetStringAsync($"/admin/reponses/{submissionId}");

        var (final, hops) = await FollowAsync(client, new HttpRequestMessage(HttpMethod.Post, $"/admin/reponses/{submissionId}?handler=Regenerate")
        {
            Content = Post(page),
        });

        await AssertAccessDeniedPageAsync(final, hops);
    }

    [Theory]
    [InlineData("export")]
    [InlineData("diffusion")]
    [InlineData("diffusion?handler=Qr&format=png")]
    public async Task Reader_opening_editor_screens_gets_access_denied_page(string path)
    {
        var slug = $"bo-refus-{Guid.NewGuid():N}"[..20];
        var (form, _) = await SeedSubmissionAsync(slug);
        var client = await LoggedInAsync(AppRoles.Reader, slug);

        var (final, hops) = await FollowAsync(client, new HttpRequestMessage(HttpMethod.Get, $"/admin/formulaires/{form.Id}/{path}"));

        await AssertAccessDeniedPageAsync(final, hops);
    }

    [Fact]
    public async Task Access_denied_page_is_reachable_without_role_or_session()
    {
        // Ouverte à tous : un compte authentifié sans rôle reste refusé par la politique du dossier /admin,
        // et repasser par cette politique ferait boucler la redirection.
        var response = await Client().GetAsync("/admin/acces-refuse");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Accès refusé", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
