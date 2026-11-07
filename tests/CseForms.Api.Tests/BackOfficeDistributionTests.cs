using System.Net;
using CseForms.Application.Settings;
using CseForms.Domain.Forms;
using CseForms.Domain.Settings;
using CseForms.Testing;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Tests;

/// <summary>Diffusion dans le back-office : QR code, flyer, droits et journal.</summary>
public partial class BackOfficeTests
{
    private const string DistributionDefinition = """
        {"sections":[{"id":"s","fields":[
          {"id":"nom","type":"text","label":"Nom","required":true},
          {"id":"site","type":"site","label":"Lieu de travail","required":true,"prefill":"url"}
        ]}]}
        """;

    private async Task<Form> SeedDistributionFormAsync(string slug, string? accessCode = null)
    {
        await using var db = factory.Database.CreateContext();
        await TestData.EnsureSiteAsync(db, "Site fictif");
        if (await db.Settings.FindAsync(SettingKeys.PublicBaseUrl) is null)
        {
            db.Settings.Add(new Setting(SettingKeys.PublicBaseUrl, "https://formulaires.example.test"));
            await db.SaveChangesAsync();
        }

        return await TestData.AddPublishedFormAsync(db, slug, DistributionDefinition, f =>
        {
            if (accessCode is not null)
            {
                f.SetAccessCodeHash(TestData.HashAccessCode(accessCode));
            }
        });
    }

    private async Task<HttpClient> EditorOnAsync(string slug)
    {
        var editor = await CreateUserAsync("editor");
        Assert.Equal(0, await RunCliAsync(string.Empty, "grant", "--email", editor, "--form", slug));
        var client = Client();
        await LoginAsync(client, editor, Password);
        return client;
    }

    [Fact]
    public async Task Editor_downloads_qr_codes_and_each_download_is_audited()
    {
        var form = await SeedDistributionFormAsync("bo-qr");
        var client = await EditorOnAsync("bo-qr");

        var page = await client.GetStringAsync($"/admin/formulaires/{form.Id}/diffusion");
        Assert.Contains("https://formulaires.example.test/f/bo-qr", page);
        Assert.Contains("<option value=\"Site fictif\">Site fictif</option>", page);
        Assert.Contains($"/admin/formulaires/{form.Id}/diffusion", await client.GetStringAsync("/admin"));

        var png = await client.GetAsync($"/admin/formulaires/{form.Id}/diffusion?handler=Qr&format=png");
        Assert.Equal(HttpStatusCode.OK, png.StatusCode);
        Assert.Equal("image/png", png.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, (await png.Content.ReadAsByteArrayAsync())[..4]);

        var svg = await client.GetAsync($"/admin/formulaires/{form.Id}/diffusion?handler=Qr&format=svg&site=Site%20fictif");
        Assert.Equal("image/svg+xml", svg.Content.Headers.ContentType?.MediaType);
        Assert.Equal("qr-bo-qr-site-fictif.svg", svg.Content.Headers.ContentDisposition?.FileNameStar ?? svg.Content.Headers.ContentDisposition?.FileName);
        var svgText = await svg.Content.ReadAsStringAsync();
        Assert.Contains("<svg", svgText);
        Assert.DoesNotContain("<script", svgText, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(2, (await AuditAsync($"form:{form.Id}")).Count(e => e.Action == "form.qrcode"));
    }

    [Fact]
    public async Task Unknown_site_is_refused_with_a_message()
    {
        var form = await SeedDistributionFormAsync("bo-qr-site");
        var client = await EditorOnAsync("bo-qr-site");

        var response = await client.GetAsync($"/admin/formulaires/{form.Id}/diffusion?handler=Qr&format=png&site=Site%20inconnu");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Site inconnu", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Flyer_needs_the_right_access_code_which_is_printed_but_never_stored()
    {
        var form = await SeedDistributionFormAsync("bo-flyer", accessCode: "482193");
        var client = await EditorOnAsync("bo-flyer");
        var url = $"/admin/formulaires/{form.Id}/diffusion?handler=Flyer";
        var token = Token(await client.GetStringAsync($"/admin/formulaires/{form.Id}/diffusion"));
        var before = factory.PdfConverter.Documents.Count;

        var wrong = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = "111111",
            ["__RequestVerificationToken"] = token,
        }));
        var wrongPage = await wrong.Content.ReadAsStringAsync();
        Assert.Contains("Code d&#x27;accès incorrect", wrongPage.Replace("'", "&#x27;", StringComparison.Ordinal));
        Assert.DoesNotContain("111111", wrongPage);
        Assert.Equal(before, factory.PdfConverter.Documents.Count);

        var flyer = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = "482193",
            ["site"] = "Site fictif",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.OK, flyer.StatusCode);
        Assert.Equal("application/pdf", flyer.Content.Headers.ContentType?.MediaType);
        Assert.Equal("flyer-bo-flyer-site-fictif.pdf", flyer.Content.Headers.ContentDisposition?.FileNameStar ?? flyer.Content.Headers.ContentDisposition?.FileName);

        var html = factory.PdfConverter.Documents[^1].Body;
        Assert.Contains("482193", html);
        Assert.Contains("formulaires.example.test/f/bo-flyer?site=Site fictif", html);

        var audit = await AuditAsync($"form:{form.Id}");
        Assert.Contains(audit, e => e.Action == "form.flyer.code_rejected");
        Assert.Contains(audit, e => e.Action == "form.flyer");

        await using var db = factory.Database.CreateContext();
        Assert.False(await db.AuditLog.AnyAsync(a => a.Target!.Contains("482193") || a.Action.Contains("482193")));
    }

    [Fact]
    public async Task Flyer_is_refused_without_antiforgery_token()
    {
        var form = await SeedDistributionFormAsync("bo-flyer-csrf");
        var client = await EditorOnAsync("bo-flyer-csrf");

        var response = await client.PostAsync($"/admin/formulaires/{form.Id}/diffusion?handler=Flyer", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Readers_and_accounts_without_rights_cannot_distribute()
    {
        var form = await SeedDistributionFormAsync("bo-diffusion-droits");

        var reader = await CreateUserAsync("reader");
        Assert.Equal(0, await RunCliAsync(string.Empty, "grant", "--email", reader, "--form", "bo-diffusion-droits"));
        var readerClient = Client();
        await LoginAsync(readerClient, reader, Password);
        Assert.DoesNotContain("/diffusion", await readerClient.GetStringAsync("/admin"));
        Assert.NotEqual(HttpStatusCode.OK, (await readerClient.GetAsync($"/admin/formulaires/{form.Id}/diffusion")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await readerClient.GetAsync($"/admin/formulaires/{form.Id}/diffusion?handler=Qr&format=png")).StatusCode);

        var outsider = await CreateUserAsync("editor");
        var outsiderClient = Client();
        await LoginAsync(outsiderClient, outsider, Password);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync($"/admin/formulaires/{form.Id}/diffusion?handler=Qr&format=png")).StatusCode);

        var anonymous = await Client().GetAsync($"/admin/formulaires/{form.Id}/diffusion");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);

        var audit = await AuditAsync($"form:{form.Id}");
        Assert.DoesNotContain(audit, e => e.Action == "form.qrcode");
        Assert.Contains(audit, e => e.Action == "access.denied");
    }
}
