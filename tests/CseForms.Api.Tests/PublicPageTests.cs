using System.Net;
using System.Text.Json.Nodes;
using CseForms.Domain.Sites;
using CseForms.Testing;

namespace CseForms.Api.Tests;

public class PublicPageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Form_page_is_served_for_any_slug_with_strict_csp()
    {
        var response = await factory.CreateClient().GetAsync("/f/inscription");
        var html = await response.Content.ReadAsStringAsync();
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("<script type=\"module\" src=\"/assets/js/app.mjs\">", html, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Page_has_no_inline_script_or_style_nor_external_resource()
    {
        var html = await factory.CreateClient().GetStringAsync("/f/inscription");

        Assert.DoesNotMatch("<script(?![^>]*\\ssrc=)[^>]*>", html);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/assets/js/app.mjs")]
    [InlineData("/assets/js/engine.mjs")]
    [InlineData("/assets/js/form-view.mjs")]
    public async Task Scripts_are_served_as_javascript(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Stylesheet_is_served() =>
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/assets/form.css")).StatusCode);

    [Theory]
    [InlineData("/form.html")]
    [InlineData("/")]
    [InlineData("/assets/../form.html")]
    public async Task Only_assets_and_form_route_are_exposed(string path) =>
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync(path)).StatusCode);

    [Fact]
    public async Task Active_sites_are_listed_only_while_form_accepts_responses()
    {
        await using (var db = factory.Database.CreateContext())
        {
            await TestData.EnsureSiteAsync(db, "Site B fictif");
            await TestData.EnsureSiteAsync(db, "Site A fictif");
            var inactive = new Site("Site fermé fictif");
            inactive.Deactivate();
            db.Sites.Add(inactive);
            await db.SaveChangesAsync();
            await TestData.AddPublishedFormAsync(db, "page-sites");
            await TestData.AddPublishedFormAsync(db, "page-sites-ferme", configure: f => f.Close());
        }

        var client = factory.CreateClient();
        var open = JsonNode.Parse(await client.GetStringAsync("/api/public/forms/page-sites"))!;
        var closed = JsonNode.Parse(await client.GetStringAsync("/api/public/forms/page-sites-ferme"))!;

        Assert.Equal(["Site A fictif", "Site B fictif"], open["sites"]!.AsArray().Select(s => s!.GetValue<string>()));
        Assert.Empty(closed["sites"]!.AsArray());
    }
}
