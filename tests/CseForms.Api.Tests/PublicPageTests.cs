using System.Net;
using System.Text.Json.Nodes;
using CseForms.Domain.Settings;
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

    [Fact]
    public async Task Theme_stylesheet_is_linked_and_follows_settings_with_revalidation()
    {
        var client = factory.CreateClient();
        Assert.Contains("<link rel=\"stylesheet\" href=\"/theme.css\">", await client.GetStringAsync("/f/inscription"));

        await using (var db = factory.Database.CreateContext())
        {
            db.Settings.RemoveRange(db.Settings.Where(s => s.Key.StartsWith("theme.")));
            db.Settings.Add(new Setting("theme.primaryColor", "#a3004c"));
            db.Settings.Add(new Setting("theme.background", "data:image/png;base64,iVBORw0KGgoAAQ=="));
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/theme.css");
        var css = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
        Assert.Contains("--primary: #a3004c;", css);
        Assert.Contains("url(\"data:image/png;base64,iVBORw0KGgoAAQ==\")", css);
        Assert.DoesNotContain("http", css);

        using var again = new HttpRequestMessage(HttpMethod.Get, "/theme.css");
        again.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);
    }

    [Fact]
    public async Task Logo_is_shown_from_the_site_itself_only_when_defined()
    {
        var client = factory.CreateClient();
        Assert.Contains("<header class=\"brand\" hidden></header>", await client.GetStringAsync("/f/inscription"));

        await using (var db = factory.Database.CreateContext())
        {
            db.Settings.RemoveRange(db.Settings.Where(s => s.Key == "cse.logo"));
            await db.SaveChangesAsync();
            await TestData.AddPublishedFormAsync(db, "page-logo");
        }

        Assert.False(JsonNode.Parse(await client.GetStringAsync("/api/public/forms/page-logo"))!["logo"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/logo")).StatusCode);

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        await using (var db = factory.Database.CreateContext())
        {
            db.Settings.Add(new Setting("cse.logo", "data:image/png;base64," + Convert.ToBase64String(png)));
            await db.SaveChangesAsync();
        }

        Assert.True(JsonNode.Parse(await client.GetStringAsync("/api/public/forms/page-logo"))!["logo"]!.GetValue<bool>());
        var response = await client.GetAsync("/logo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());

        using var again = new HttpRequestMessage(HttpMethod.Get, "/logo");
        again.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);
    }

    [Fact]
    public async Task Privacy_notice_page_and_link_follow_the_setting()
    {
        var client = factory.CreateClient();
        Assert.Contains("<footer class=\"legal\" hidden></footer>", await client.GetStringAsync("/f/inscription"));

        await using (var db = factory.Database.CreateContext())
        {
            db.Settings.RemoveRange(db.Settings.Where(s => s.Key == "rgpd.notice"));
            await db.SaveChangesAsync();
            await TestData.AddPublishedFormAsync(db, "page-notice");
        }

        var missing = await client.GetAsync("/donnees-personnelles");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("Notice indisponible", await missing.Content.ReadAsStringAsync());
        Assert.False(JsonNode.Parse(await client.GetStringAsync("/api/public/forms/page-notice"))!["privacyNotice"]!.GetValue<bool>());

        await using (var db = factory.Database.CreateContext())
        {
            db.Settings.Add(new Setting("rgpd.notice", "## Qui traite vos données ?\nLe CSE fictif. <script>alert(1)</script>\n\n- [CNIL](https://www.cnil.fr)"));
            await db.SaveChangesAsync();
        }

        Assert.True(JsonNode.Parse(await client.GetStringAsync("/api/public/forms/page-notice"))!["privacyNotice"]!.GetValue<bool>());
        var response = await client.GetAsync("/donnees-personnelles");
        var html = await response.Content.ReadAsStringAsync();
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
        Assert.Contains("<h1>Vos données personnelles</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<h2>Qui traite vos données ?</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://www.cnil.fr\" rel=\"noopener noreferrer\">CNIL</a>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/theme.css\">", html, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
    }

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
