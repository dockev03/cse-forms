using System.Net;
using System.Text;
using CseForms.Domain.Forms;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Tests;

/// <summary>Éditeur visuel : création, modification d'une version en ligne, erreurs du contrôle, droits, en-têtes.</summary>
public partial class BackOfficeTests
{
    private const string NewFormEditorUrl = "/admin/formulaires/nouveau";

    private static string EditorUrl(Guid formId) => $"/admin/formulaires/{formId}/editeur";

    /// <summary>Envoi du fichier comme le fait la page (multipart), avec le jeton anti-CSRF de la page.</summary>
    private static async Task<HttpResponseMessage> PostEditorAsync(HttpClient client, string url, string package)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(Token(await client.GetStringAsync(url))), "__RequestVerificationToken" },
            { new StringContent(package, Encoding.UTF8), "package" },
        };
        return await client.PostAsync(url, content);
    }

    [Fact]
    public async Task Admin_creates_a_form_in_the_editor_which_stays_offline_until_published_from_the_preview()
    {
        var admin = await LoggedInAsync("admin");
        Assert.Contains(NewFormEditorUrl, await admin.GetStringAsync("/admin"));

        var page = await admin.GetAsync(NewFormEditorUrl);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("<script type=\"module\" src=\"/assets/js/editor.mjs\"></script>", html);
        Assert.Contains("id=\"package\" name=\"package\"", html);
        Assert.Contains("&quot;retentionDays&quot;: 180", html);
        var csp = page.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self';", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("no-store", page.Headers.CacheControl!.ToString());

        var saved = await PostEditorAsync(admin, NewFormEditorUrl, ImportFile("bo-editeur", label: "Nom fictif"));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var location = saved.Headers.Location!.OriginalString;
        Assert.Contains("fait=editeur", location);
        var formId = Guid.Parse(location.Split('/')[3]);

        Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync("/api/public/forms/bo-editeur")).StatusCode);
        var preview = await admin.GetStringAsync(location);
        Assert.Contains("Brouillon enregistré depuis l", preview);
        Assert.Contains(EditorUrl(formId), preview);

        Assert.Equal(HttpStatusCode.Redirect, (await PostPreviewAsync(admin, formId, "Publish")).StatusCode);
        Assert.True(await AcceptingAsync("bo-editeur"));

        await using var db = factory.Database.CreateContext();
        Assert.True(await db.AuditLog.AnyAsync(a => a.Target == $"form:{formId}" && a.Action == "form.draft.save" && a.Detail == "create;editor"));
    }

    [Fact]
    public async Task Assigned_editor_edits_the_online_version_and_sees_the_import_errors_on_the_page()
    {
        var form = await SeedLifecycleFormAsync("bo-editeur-version");
        var email = await CreateUserAsync("editor");
        Assert.Equal(0, await RunCliAsync(string.Empty, "grant", "--email", email, "--form", "bo-editeur-version"));
        var editor = Client();
        await LoginAsync(editor, email, Password);

        Assert.Contains(EditorUrl(form.Id), await editor.GetStringAsync($"/admin/formulaires/{form.Id}/reglages"));
        var html = await editor.GetStringAsync(EditorUrl(form.Id));
        Assert.Contains("Vous partez de la version en ligne", html);
        Assert.Contains("data-slug=\"bo-editeur-version\"", html);

        // Erreurs du contrôle : la page les liste avec leur chemin et conserve le fichier envoyé.
        var invalid = ImportFile("bo-editeur-version").Replace("\"label\":\"Nom\"", "\"label\":\"\"");
        var refused = await PostEditorAsync(editor, EditorUrl(form.Id), invalid);
        var refusedHtml = await refused.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("data-path=\"$.sections[0].fields[0].label\"", refusedHtml);
        Assert.Contains("Le libellé est obligatoire.", refusedHtml);

        // Une autre adresse ne peut pas être visée depuis l'éditeur d'un formulaire.
        var otherSlug = await PostEditorAsync(editor, EditorUrl(form.Id), ImportFile("bo-autre-adresse"));
        Assert.Contains("ne se modifie pas", await otherSlug.Content.ReadAsStringAsync());

        var saved = await PostEditorAsync(editor, EditorUrl(form.Id), ImportFile("bo-editeur-version", label: "Nom modifié"));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("Vous modifiez le brouillon en cours", await editor.GetStringAsync(EditorUrl(form.Id)));

        await using var db = factory.Database.CreateContext();
        Assert.Contains("Nom modifié", (await db.FormDrafts.SingleAsync(d => d.FormId == form.Id)).PackageJson);
        Assert.False(await db.Forms.AnyAsync(f => f.Slug == "bo-autre-adresse"));
        Assert.Equal(FormStatus.Published, (await db.Forms.SingleAsync(f => f.Id == form.Id)).Status);
    }

    [Fact]
    public async Task Editor_page_is_refused_to_unassigned_accounts_readers_and_editors_creating_a_form()
    {
        var form = await SeedLifecycleFormAsync("bo-editeur-droits");

        var stranger = await LoggedInAsync("editor");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(EditorUrl(form.Id))).StatusCode);
        Assert.Equal(BackOfficeAccessDenied, (await stranger.GetAsync(NewFormEditorUrl)).Headers.Location?.AbsolutePath);
        Assert.DoesNotContain(NewFormEditorUrl, await stranger.GetStringAsync("/admin"));

        var readerEmail = await CreateUserAsync("reader");
        Assert.Equal(0, await RunCliAsync(string.Empty, "grant", "--email", readerEmail, "--form", "bo-editeur-droits"));
        var reader = Client();
        await LoginAsync(reader, readerEmail, Password);
        Assert.Equal(BackOfficeAccessDenied, (await reader.GetAsync(EditorUrl(form.Id))).Headers.Location?.AbsolutePath);

        var anonymous = await Client().GetAsync(EditorUrl(form.Id));
        Assert.Contains("/admin/connexion", anonymous.Headers.Location?.OriginalString);

        // Sans jeton anti-CSRF, rien n'est enregistré.
        var admin = await LoggedInAsync("admin");
        var content = new MultipartFormDataContent { { new StringContent(ImportFile("bo-editeur-droits", label: "Sans jeton")), "package" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(EditorUrl(form.Id), content)).StatusCode);

        await using var db = factory.Database.CreateContext();
        Assert.False(await db.FormDrafts.AnyAsync(d => d.FormId == form.Id));
        Assert.Contains(await AuditAsync($"form:{form.Id}"), e => e.Action == "access.denied");
        Assert.True(await db.AuditLog.AnyAsync(a => a.Action == "access.denied" && a.Target == null && a.Detail == "form-editor"));
    }
}
