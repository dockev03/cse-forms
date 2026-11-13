using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Tests;

/// <summary>Écran « Paramètres » du back-office : coordonnées, logo, sites, droits, journal.</summary>
public partial class BackOfficeTests
{
    private const string SettingsUrl = "/admin/parametres";

    private static readonly byte[] FictitiousPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static async Task<HttpResponseMessage> PostSettingsAsync(HttpClient client, string handler, Dictionary<string, string>? fields = null)
    {
        var values = new Dictionary<string, string>(fields ?? [])
        {
            ["__RequestVerificationToken"] = Token(await client.GetStringAsync(SettingsUrl)),
        };
        return await client.PostAsync($"{SettingsUrl}?handler={handler}", new FormUrlEncodedContent(values));
    }

    private static Task<HttpResponseMessage> PostLogoAsync(HttpClient client, byte[] content, string fileName, string mediaType) =>
        PostImageAsync(client, "Logo", "logo", content, fileName, mediaType);

    private static async Task<HttpResponseMessage> PostImageAsync(HttpClient client, string handler, string field, byte[] content, string fileName, string mediaType)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(file, field, fileName);
        form.Add(new StringContent(Token(await client.GetStringAsync(SettingsUrl))), "__RequestVerificationToken");
        return await client.PostAsync($"{SettingsUrl}?handler={handler}", form);
    }

    [Fact]
    public async Task Admin_edits_cse_details_public_and_alert_addresses()
    {
        var client = await LoggedInAsync("admin");
        Assert.Contains($"href=\"{SettingsUrl}\"", await client.GetStringAsync("/admin"));

        var saved = await PostSettingsAsync(client, "Save", new()
        {
            ["cse.name"] = "CSE fictif",
            ["cse.office"] = "Secrétaire : Prénom Fictif\r\nTrésorier : Autre Fictif",
            ["app.publicBaseUrl"] = "https://formulaires.example.test/",
            ["alerts.email"] = "alertes@example.test",
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var page = await client.GetStringAsync(saved.Headers.Location!.OriginalString);
        Assert.Contains("Paramètres enregistrés", page);
        Assert.Contains("value=\"https://formulaires.example.test\"", page);

        var refused = await PostSettingsAsync(client, "Save", new()
        {
            ["cse.name"] = "Autre nom fictif",
            ["app.publicBaseUrl"] = "http://formulaires.example.test",
        });
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        var refusedPage = await refused.Content.ReadAsStringAsync();
        Assert.Contains("Rien n'a été enregistré", refusedPage);
        Assert.Contains("value=\"Autre nom fictif\"", refusedPage);

        Assert.Equal(HttpStatusCode.Redirect, (await PostSettingsAsync(client, "Save", new() { ["alerts.email"] = "" })).StatusCode);

        await using var db = factory.Database.CreateContext();
        var values = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        Assert.Equal("CSE fictif", values["cse.name"]);
        Assert.Equal("Secrétaire : Prénom Fictif\nTrésorier : Autre Fictif", values["cse.office"]);
        Assert.Equal("https://formulaires.example.test", values["app.publicBaseUrl"]);
        Assert.False(values.ContainsKey("alerts.email"));
        Assert.False(await db.AuditLog.AnyAsync(a => a.Detail != null && (a.Detail.Contains("@") || a.Detail.Contains("Fictif"))));
        Assert.Contains(await AuditAsync("settings"), e => e.Action == "settings.change");
    }

    [Fact]
    public async Task Admin_uploads_and_removes_the_logo_but_not_an_svg()
    {
        var client = await LoggedInAsync("admin");

        var svg = await PostLogoAsync(client, "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>"u8.ToArray(), "logo.png", "image/png");
        Assert.Equal(HttpStatusCode.OK, svg.StatusCode);
        Assert.Contains("PNG ou JPEG", await svg.Content.ReadAsStringAsync());

        var png = await PostLogoAsync(client, FictitiousPng, "logo.png", "image/png");
        Assert.Equal(HttpStatusCode.Redirect, png.StatusCode);
        Assert.Contains("src=\"data:image/png;base64,", await client.GetStringAsync(png.Headers.Location!.OriginalString));

        Assert.Equal(HttpStatusCode.Redirect, (await PostSettingsAsync(client, "RemoveLogo")).StatusCode);
        await using var db = factory.Database.CreateContext();
        Assert.False(await db.Settings.AnyAsync(s => s.Key == "cse.logo"));
    }

    [Fact]
    public async Task Admin_sets_a_contrasted_color_and_a_background_image()
    {
        var client = await LoggedInAsync("admin");

        var light = await PostSettingsAsync(client, "Theme", new() { ["theme.primaryColor"] = "#FFCC00" });
        Assert.Equal(HttpStatusCode.OK, light.StatusCode);
        var lightPage = await light.Content.ReadAsStringAsync();
        Assert.Contains("Couleur principale : Contraste insuffisant", lightPage);
        Assert.Contains("value=\"#FFCC00\"", lightPage);

        var dark = await PostSettingsAsync(client, "Theme", new() { ["theme.primaryColor"] = "#A3004C" });
        Assert.Equal(HttpStatusCode.Redirect, dark.StatusCode);
        Assert.Contains("value=\"#a3004c\"", await client.GetStringAsync(dark.Headers.Location!.OriginalString));

        var svg = await PostImageAsync(client, "Background", "background", "<svg onload=\"alert(1)\"/>"u8.ToArray(), "fond.png", "image/png");
        Assert.Contains("Image de fond refusée", await svg.Content.ReadAsStringAsync());

        var png = await PostImageAsync(client, "Background", "background", FictitiousPng, "fond.png", "image/png");
        Assert.Equal(HttpStatusCode.Redirect, png.StatusCode);
        Assert.Contains("class=\"background-preview\" src=\"data:image/png;base64,", await client.GetStringAsync(png.Headers.Location!.OriginalString));

        var css = await client.GetStringAsync("/theme.css");
        Assert.Contains("--primary: #a3004c;", css);
        Assert.Contains("background-image: url(\"data:image/png;base64,", css);

        Assert.Equal(HttpStatusCode.Redirect, (await PostSettingsAsync(client, "RemoveBackground")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostSettingsAsync(client, "Theme", new() { ["theme.primaryColor"] = string.Empty })).StatusCode);
        Assert.DoesNotContain("--primary", await client.GetStringAsync("/theme.css"));
    }

    [Fact]
    public async Task Admin_adds_disables_and_enables_a_site()
    {
        var client = await LoggedInAsync("admin");

        Assert.Equal(HttpStatusCode.Redirect, (await PostSettingsAsync(client, "AddSite", new() { ["label"] = " Site paramètres fictif " })).StatusCode);
        var duplicate = await PostSettingsAsync(client, "AddSite", new() { ["label"] = "SITE PARAMÈTRES FICTIF" });
        Assert.Contains("existe déjà", await duplicate.Content.ReadAsStringAsync());

        Guid siteId;
        await using (var db = factory.Database.CreateContext())
        {
            siteId = (await db.Sites.AsNoTracking().SingleAsync(s => s.Label == "Site paramètres fictif")).Id;
        }

        Assert.Equal(HttpStatusCode.Redirect, (await PostSettingsAsync(client, "DisableSite", new() { ["siteId"] = siteId.ToString() })).StatusCode);
        await using (var db = factory.Database.CreateContext())
        {
            Assert.False((await db.Sites.AsNoTracking().SingleAsync(s => s.Id == siteId)).IsActive);
        }

        Assert.Equal(HttpStatusCode.Redirect, (await PostSettingsAsync(client, "EnableSite", new() { ["siteId"] = siteId.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostSettingsAsync(client, "DisableSite", new() { ["siteId"] = Guid.NewGuid().ToString() })).StatusCode);

        var actions = (await AuditAsync($"site:{siteId}")).Select(e => e.Action).ToList();
        Assert.Equal(["site.add", "site.disable", "site.enable"], actions);
    }

    [Theory]
    [InlineData("editor")]
    [InlineData("reader")]
    public async Task Settings_are_hidden_from_non_administrators(string role)
    {
        var client = await LoggedInAsync(role);

        Assert.DoesNotContain(SettingsUrl, await client.GetStringAsync("/admin"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(SettingsUrl)).StatusCode);
        var post = await client.PostAsync($"{SettingsUrl}?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string> { ["cse.name"] = "Pirate" }));
        Assert.NotEqual(HttpStatusCode.Redirect, post.StatusCode);

        await using var db = factory.Database.CreateContext();
        Assert.False(await db.Settings.AnyAsync(s => s.Value == "Pirate"));
    }

    [Fact]
    public async Task Forged_settings_post_without_token_is_refused()
    {
        var client = await LoggedInAsync("admin");

        var forged = await client.PostAsync($"{SettingsUrl}?handler=AddSite", new FormUrlEncodedContent(new Dictionary<string, string> { ["label"] = "Site forgé fictif" }));

        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        await using var db = factory.Database.CreateContext();
        Assert.False(await db.Sites.AnyAsync(s => s.Label == "Site forgé fictif"));
    }
}
