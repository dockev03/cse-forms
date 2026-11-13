using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CseForms.Domain.Forms;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Tests;

/// <summary>Brouillon et aperçu avant publication : enregistrement par l'import, cadre, données, droits, publication.</summary>
public partial class BackOfficeTests
{
    private static string PreviewUrl(Guid formId) => $"/admin/formulaires/{formId}/apercu";

    private async Task<Guid> SaveDraftAsync(HttpClient client, string json)
    {
        var response = await UploadAsync(client, Encoding.UTF8.GetBytes(json), handler: "Draft");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.Contains("fait=brouillon", location);
        return Guid.Parse(location.Split('/')[3]);
    }

    /// <summary>Libellé du premier champ, dans la réponse de l'API publique ou des données de l'aperçu.</summary>
    private static string FirstLabel(string json) =>
        JsonNode.Parse(json)!["definition"]!["sections"]![0]!["fields"]![0]!["label"]!.GetValue<string>();

    private static async Task<HttpResponseMessage> PostPreviewAsync(HttpClient client, Guid formId, string handler, Dictionary<string, string>? fields = null)
    {
        var values = new Dictionary<string, string>(fields ?? [])
        {
            ["__RequestVerificationToken"] = Token(await client.GetStringAsync(PreviewUrl(formId))),
        };
        return await client.PostAsync($"{PreviewUrl(formId)}?handler={handler}", new FormUrlEncodedContent(values));
    }

    [Fact]
    public async Task Admin_saves_a_new_form_as_draft_previews_it_and_it_stays_offline_until_published()
    {
        var client = await LoggedInAsync("admin");
        var formId = await SaveDraftAsync(client, ImportFile("bo-apercu", label: "Nom fictif"));

        // Hors ligne : le lien public ne révèle pas le brouillon.
        Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync("/api/public/forms/bo-apercu")).StatusCode);

        var page = await client.GetAsync($"{PreviewUrl(formId)}?fait=brouillon");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Brouillon enregistré, rien n", html);
        Assert.Contains($"src=\"/admin/formulaires/{formId}/apercu/cadre\"", html);
        Assert.Contains("preview-frame mobile", html);
        Assert.Contains("frame-src 'self'", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("no-store", page.Headers.CacheControl!.ToString());
        Assert.Contains("preview-frame desktop", await client.GetStringAsync($"{PreviewUrl(formId)}?vue=ordinateur"));

        var frame = await client.GetAsync($"{PreviewUrl(formId)}/cadre");
        Assert.Equal(HttpStatusCode.OK, frame.StatusCode);
        Assert.Contains("/assets/js/preview.mjs", await frame.Content.ReadAsStringAsync());
        Assert.Contains("frame-ancestors 'self'", frame.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("SAMEORIGIN", frame.Headers.GetValues("X-Frame-Options").Single());

        var data = await client.GetAsync($"{PreviewUrl(formId)}/donnees");
        Assert.Equal(HttpStatusCode.OK, data.StatusCode);
        Assert.Contains("no-store", data.Headers.CacheControl!.ToString());
        var json = JsonNode.Parse(await data.Content.ReadAsStringAsync())!;
        Assert.True(json["preview"]!.GetValue<bool>());
        Assert.Equal("bo-apercu", json["slug"]!.GetValue<string>());
        Assert.Equal("Nom fictif", FirstLabel(json.ToJsonString()));

        var published = await PostPreviewAsync(client, formId, "Publish");
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.Contains("fait=brouillon-publie", published.Headers.Location?.OriginalString);
        Assert.True(await AcceptingAsync("bo-apercu"));

        await using var db = factory.Database.CreateContext();
        Assert.False(await db.FormDrafts.AnyAsync(d => d.FormId == formId));
        var actions = (await AuditAsync($"form:{formId}")).Select(e => e.Action).ToList();
        Assert.Equal(["form.draft.save", "form.import"], actions);
        Assert.True(await db.AuditLog.AnyAsync(a => a.Target == $"form:{formId}" && a.Detail == "v1;draft"));
    }

    [Fact]
    public async Task Editor_drafts_a_new_version_the_online_one_is_unchanged_until_publication()
    {
        var form = await SeedLifecycleFormAsync("bo-apercu-version");
        var client = await EditorOnAsync("bo-apercu-version");
        await SaveDraftAsync(client, ImportFile("bo-apercu-version", label: "Libellé du brouillon"));

        var online = FirstLabel(await Client().GetStringAsync("/api/public/forms/bo-apercu-version"));
        Assert.NotEqual("Libellé du brouillon", online);
        Assert.Equal("Libellé du brouillon", FirstLabel(await client.GetStringAsync($"{PreviewUrl(form.Id)}/donnees")));
        Assert.Contains($"{PreviewUrl(form.Id)}\"", await client.GetStringAsync($"/admin/formulaires/{form.Id}/reglages"));

        var published = await PostPreviewAsync(client, form.Id, "Publish");
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.Equal("Libellé du brouillon", FirstLabel(await Client().GetStringAsync("/api/public/forms/bo-apercu-version")));
        await using var db = factory.Database.CreateContext();
        Assert.Equal(2, await db.FormVersions.CountAsync(v => v.FormId == form.Id));
    }

    [Fact]
    public async Task Protected_draft_asks_the_access_code_at_publication_and_never_shows_it_back()
    {
        var client = await LoggedInAsync("admin");
        var formId = await SaveDraftAsync(client, ImportFile("bo-apercu-code", mode: "pin"));
        Assert.Contains("name=\"code\"", await client.GetStringAsync(PreviewUrl(formId)));

        var mismatch = await PostPreviewAsync(client, formId, "Publish", new() { ["code"] = "582941", ["confirmation"] = "582942" });
        var html = await mismatch.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, mismatch.StatusCode);
        Assert.Contains("différentes", html);
        Assert.DoesNotContain("582941", html);

        var published = await PostPreviewAsync(client, formId, "Publish", new() { ["code"] = "582941", ["confirmation"] = "582941" });
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        await using var db = factory.Database.CreateContext();
        Assert.Equal(FormStatus.Published, (await db.Forms.AsNoTracking().SingleAsync(f => f.Id == formId)).Status);
        Assert.False(await db.AuditLog.AnyAsync(a => a.Detail != null && a.Detail.Contains("582941")));
    }

    [Fact]
    public async Task Deleting_a_draft_keeps_the_online_version()
    {
        var form = await SeedLifecycleFormAsync("bo-apercu-suppr");
        var client = await LoggedInAsync("admin");
        await SaveDraftAsync(client, ImportFile("bo-apercu-suppr", label: "À jeter"));

        var deleted = await PostPreviewAsync(client, form.Id, "Delete");

        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Contains("fait=brouillon-supprime", deleted.Headers.Location?.OriginalString);
        Assert.Contains("n'a pas de brouillon", await client.GetStringAsync(PreviewUrl(form.Id)));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{PreviewUrl(form.Id)}/donnees")).StatusCode);
        Assert.True(await AcceptingAsync("bo-apercu-suppr"));
        Assert.Contains(await AuditAsync($"form:{form.Id}"), e => e.Action == "form.draft.delete");
    }

    [Fact]
    public async Task Unassigned_editor_reader_and_anonymous_cannot_see_a_draft()
    {
        var form = await SeedLifecycleFormAsync("bo-apercu-droits");
        var admin = await LoggedInAsync("admin");
        await SaveDraftAsync(admin, ImportFile("bo-apercu-droits", label: "Secret de brouillon"));

        var stranger = await LoggedInAsync("editor");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(PreviewUrl(form.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"{PreviewUrl(form.Id)}/donnees")).StatusCode);

        // Lecteur attribué au formulaire : page « Accès refusé », aucune donnée du brouillon.
        var readerEmail = await CreateUserAsync("reader");
        Assert.Equal(0, await RunCliAsync(string.Empty, "grant", "--email", readerEmail, "--form", "bo-apercu-droits"));
        var reader = Client();
        await LoginAsync(reader, readerEmail, Password);
        Assert.Equal(BackOfficeAccessDenied, (await reader.GetAsync(PreviewUrl(form.Id))).Headers.Location?.AbsolutePath);
        var readerData = await reader.GetAsync($"{PreviewUrl(form.Id)}/donnees");
        Assert.Equal(BackOfficeAccessDenied, readerData.Headers.Location?.AbsolutePath);

        var anonymous = await Client().GetAsync($"{PreviewUrl(form.Id)}/donnees");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/admin/connexion", anonymous.Headers.Location?.OriginalString);

        Assert.Contains(await AuditAsync($"form:{form.Id}"), e => e.Action == "access.denied");
        await using var db = factory.Database.CreateContext();
        Assert.True(await db.FormDrafts.AnyAsync(d => d.FormId == form.Id));
    }
}
