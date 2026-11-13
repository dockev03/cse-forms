using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CseForms.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CseForms.Api.Tests;

/// <summary>Import d'un fichier de formulaire depuis le back-office : envoi multipart, droits, contrôles, journal.</summary>
public partial class BackOfficeTests
{
    private const string ImportUrl = "/admin/formulaires/import";

    private static string ImportFile(string slug, string mode = "public", string label = "Nom", string extra = "") =>
        $$"""
        {"slug":"{{slug}}","title":"Formulaire importé fictif","access":{"mode":"{{mode}}"},"retentionDays":30{{extra}},
         "sections":[{"id":"s","fields":[{"id":"nom","type":"text","label":"{{label}}","required":true}]}]}
        """;

    private async Task<HttpClient> LoggedInAsync(string role)
    {
        var email = await CreateUserAsync(role);
        var client = Client();
        await LoginAsync(client, email, Password);
        return client;
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        byte[]? definition,
        byte[]? template = null,
        Dictionary<string, string>? fields = null,
        bool withToken = true,
        string? handler = null)
    {
        using var content = new MultipartFormDataContent();
        if (definition is not null)
        {
            var file = new ByteArrayContent(definition);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            content.Add(file, "definition", "formulaire.json");
        }

        if (template is not null)
        {
            var file = new ByteArrayContent(template);
            file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            content.Add(file, "pdfTemplate", "formulaire.pdf.html");
        }

        foreach (var (name, value) in fields ?? [])
        {
            content.Add(new StringContent(value), name);
        }

        if (withToken)
        {
            content.Add(new StringContent(Token(await client.GetStringAsync(ImportUrl))), "__RequestVerificationToken");
        }

        return await client.PostAsync(handler is null ? ImportUrl : $"{ImportUrl}?handler={handler}", content);
    }

    [Fact]
    public async Task Admin_creates_a_protected_form_by_upload_and_it_is_online()
    {
        var client = await LoggedInAsync("admin");
        Assert.Contains(ImportUrl, await client.GetStringAsync("/admin"));
        var json = Encoding.UTF8.GetBytes(ImportFile("bo-import", mode: "pin", extra: ",\"pdfTemplate\":\"formulaire.pdf.html\""));

        var needsCode = await UploadAsync(client, json, Encoding.UTF8.GetBytes("<p>{{ field.nom }}</p>"));
        Assert.Contains("pas encore défini", await needsCode.Content.ReadAsStringAsync());

        var created = await UploadAsync(
            client,
            json,
            Encoding.UTF8.GetBytes("<p>{{ field.nom }}</p>"),
            new() { ["code"] = "582941", ["confirmation"] = "582941" });
        var page = await created.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Contains("Formulaire créé et publié en version 1", page);
        Assert.DoesNotContain("582941", page);

        Assert.True(await AcceptingAsync("bo-import"));
        await using var db = factory.Database.CreateContext();
        var form = await db.Forms.Include(f => f.Versions).AsNoTracking().SingleAsync(f => f.Slug == "bo-import");
        Assert.Equal("<p>{{ field.nom }}</p>", form.CurrentVersion!.PdfTemplateHtml);
        Assert.True(factory.Services.GetRequiredService<IAccessCodeHasher>().Verify(form.AccessCodeHash!, "582941"));
        var actions = (await AuditAsync($"form:{form.Id}")).Select(e => e.Action).ToList();
        Assert.Equal(["form.import", "form.access_code"], actions);
        Assert.False(await db.AuditLog.AnyAsync(a => a.Detail != null && a.Detail.Contains("582941")));
    }

    [Fact]
    public async Task Editor_publishes_a_new_version_of_an_assigned_form_but_cannot_create_one()
    {
        var form = await SeedLifecycleFormAsync("bo-import-version");
        var client = await EditorOnAsync("bo-import-version");

        var version = await UploadAsync(client, Encoding.UTF8.GetBytes(ImportFile("bo-import-version", label: "Nom de famille")));
        Assert.Contains("Version 2 publiée", await version.Content.ReadAsStringAsync());
        Assert.Contains(await AuditAsync($"form:{form.Id}"), e => e.Action == "form.import");

        var creation = await UploadAsync(client, Encoding.UTF8.GetBytes(ImportFile("bo-import-nouveau")));
        Assert.Contains("Seul un administrateur peut créer un formulaire", await creation.Content.ReadAsStringAsync());
        await using var db = factory.Database.CreateContext();
        Assert.False(await db.Forms.AnyAsync(f => f.Slug == "bo-import-nouveau"));
    }

    [Fact]
    public async Task Editor_cannot_version_a_form_of_someone_else_through_its_slug()
    {
        var form = await SeedLifecycleFormAsync("bo-import-autrui");
        var client = await LoggedInAsync("editor");

        var response = await UploadAsync(client, Encoding.UTF8.GetBytes(ImportFile("bo-import-autrui", label: "Champ détourné")));

        Assert.Contains("ne vous est pas attribué", await response.Content.ReadAsStringAsync());
        await using var db = factory.Database.CreateContext();
        Assert.Single(await db.FormVersions.Where(v => v.FormId == form.Id).ToListAsync());
        Assert.Contains(await AuditAsync($"form:{form.Id}"), e => e.Action == "access.denied");
    }

    [Fact]
    public async Task Invalid_definition_too_large_or_non_utf8_files_are_refused_with_messages()
    {
        var client = await LoggedInAsync("admin");

        var invalid = await (await UploadAsync(client, Encoding.UTF8.GetBytes("""{"slug":"bo-import-faux","inconnu":true}"""))).Content.ReadAsStringAsync();
        Assert.Contains("Propriété inconnue", invalid);

        var tooLarge = await (await UploadAsync(client, new byte[300 * 1024])).Content.ReadAsStringAsync();
        Assert.Contains("dépasse 256 Ko", tooLarge);

        var latin1 = await (await UploadAsync(client, [0x7B, 0xE9, 0x7D])).Content.ReadAsStringAsync();
        Assert.Contains("UTF-8", latin1);

        var missing = await (await UploadAsync(client, definition: null)).Content.ReadAsStringAsync();
        Assert.Contains("obligatoire", missing);
    }

    [Fact]
    public async Task Readers_and_forged_posts_cannot_import()
    {
        var reader = await LoggedInAsync("reader");
        Assert.DoesNotContain(ImportUrl, await reader.GetStringAsync("/admin"));
        Assert.Equal(BackOfficeAccessDenied, (await reader.GetAsync(ImportUrl)).Headers.Location?.AbsolutePath);

        var admin = await LoggedInAsync("admin");
        var forged = await UploadAsync(admin, Encoding.UTF8.GetBytes(ImportFile("bo-import-csrf")), withToken: false);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);

        await using var db = factory.Database.CreateContext();
        Assert.False(await db.Forms.AnyAsync(f => f.Slug == "bo-import-csrf"));
    }
}
