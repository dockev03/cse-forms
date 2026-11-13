using CseForms.Application.BackOffice;
using CseForms.Application.FormAdministration;
using CseForms.Domain.Forms;

namespace CseForms.Application.Tests;

/// <summary>
/// Éditeur visuel : chargement (brouillon, version en ligne ou fichier vierge) et enregistrement comme brouillon,
/// avec les contrôles et les droits de l'import. Partage les doublures de <see cref="FormDraftTests"/>.
/// </summary>
public partial class FormDraftTests
{
    private const string PdfTemplate = "<p>{{ field.nom }}</p>";

    private FormEditorService Editor_() => new(_backOffice, _store, Service(), _audit);

    private Task<EditorSaveResult> EditorSaveAsync(CurrentUser user, Guid? formId, string json) =>
        Editor_().SaveAsync(user, formId, json, CancellationToken.None);

    private static string FileWithTemplate(string label = "Nom") => File(label: label).Replace("\"retentionDays\"", "\"pdfTemplate\":\"essai.pdf.html\",\"retentionDays\"");

    [Fact]
    public async Task Editor_creates_a_new_form_as_an_offline_draft_through_the_import_checks()
    {
        var blank = await Editor_().GetNewAsync(Admin, CancellationToken.None);
        Assert.NotNull(blank);
        Assert.Equal(EditorSource.New, blank.Source);
        Assert.Contains("\"retentionDays\":180", blank.PackageJson);

        // Le fichier vierge n'a aucun champ : il est refusé par le contrôle, comme à l'import.
        var refused = Assert.IsType<EditorSaveResult.Invalid>(await EditorSaveAsync(Admin, null, FormEditorService.BlankPackage("essai-vierge", "Vierge")));
        Assert.Contains(refused.Errors, e => e.Path == "$.sections[0].fields");
        Assert.Empty(_store.Forms);

        var saved = Assert.IsType<EditorSaveResult.Saved>(await EditorSaveAsync(Admin, null, File()));
        var form = Assert.Single(_store.Forms);
        Assert.Equal((saved.FormId, FormStatus.Draft), (form.Id, form.Status));
        Assert.Empty(form.Versions);
        Assert.Equal(("form.draft.save", $"form:{form.Id}", "create;editor"), Assert.Single(_audit.Entries));
    }

    [Fact]
    public async Task New_form_from_the_editor_never_overwrites_an_existing_form()
    {
        var form = await PublishedFormAsync();

        var result = Assert.IsType<EditorSaveResult.Invalid>(await EditorSaveAsync(Admin, null, File(label: "Autre")));

        Assert.Equal(("$.slug", FormDuplicationService.SlugTaken), (Assert.Single(result.Errors).Path, result.Errors[0].Message));
        Assert.Empty(_store.Drafts);
        Assert.Single(form.Versions);
    }

    [Fact]
    public async Task Editor_loads_the_published_version_then_the_draft_and_keeps_the_pdf_template()
    {
        var form = await PublishedFormAsync();

        var fromVersion = await Editor_().GetAsync(Admin, form.Id, CancellationToken.None);
        Assert.Equal(LifecycleStatus.Ok, fromVersion.Status);
        Assert.Equal((EditorSource.Version, "essai-brouillon"), (fromVersion.Value!.Source, fromVersion.Value.Slug));
        Assert.Equal(["nom", "site"], fromVersion.Value.PublishedFieldIds);
        Assert.Contains("\"label\":\"Nom\"", fromVersion.Value.PackageJson);

        Assert.IsType<DraftSaveResult.Saved>(await SaveAsync(Admin, FileWithTemplate("Nom de famille"), PdfTemplate));
        var fromDraft = (await Editor_().GetAsync(Admin, form.Id, CancellationToken.None)).Value!;
        Assert.Equal(EditorSource.Draft, fromDraft.Source);
        Assert.True(fromDraft.HasPdfTemplate);
        Assert.Contains("Nom de famille", fromDraft.PackageJson);

        _audit.Entries.Clear();
        Assert.IsType<EditorSaveResult.Saved>(await EditorSaveAsync(Admin, form.Id, FileWithTemplate("Nom d'usage")));
        var draft = Assert.Single(_store.Drafts);
        Assert.Contains("Nom d'usage", draft.PackageJson);
        Assert.Equal(PdfTemplate, draft.PdfTemplateHtml);
        Assert.Single(form.Versions);
        Assert.Equal(("form.draft.save", $"form:{form.Id}", "editor"), Assert.Single(_audit.Entries));
    }

    [Fact]
    public async Task Editor_refuses_a_file_that_targets_another_slug()
    {
        var form = await PublishedFormAsync();

        var result = Assert.IsType<EditorSaveResult.Invalid>(
            await EditorSaveAsync(Admin, form.Id, File().Replace("essai-brouillon", "autre-adresse")));

        Assert.Equal(("$.slug", FormEditorService.SlugLocked), (Assert.Single(result.Errors).Path, result.Errors[0].Message));
        Assert.Empty(_store.Drafts);
        Assert.Single(_store.Forms);
    }

    [Fact]
    public async Task Editor_rights_follow_the_import_rules()
    {
        Assert.Null(await Editor_().GetNewAsync(Editor, CancellationToken.None));
        Assert.IsType<EditorSaveResult.Forbidden>(await EditorSaveAsync(Editor, null, File()));
        Assert.Empty(_store.Forms);
        Assert.All(_audit.Entries, e => Assert.Equal(("access.denied", null, "form-editor"), e));

        var form = await PublishedFormAsync();
        Assert.Equal(LifecycleStatus.Forbidden, (await Editor_().GetAsync(Reader, form.Id, CancellationToken.None)).Status);
        Assert.IsType<EditorSaveResult.Forbidden>(await EditorSaveAsync(Reader, form.Id, File(label: "Autre")));

        _backOffice.CanAccess = false;
        Assert.Equal(LifecycleStatus.NotFound, (await Editor_().GetAsync(Editor, form.Id, CancellationToken.None)).Status);
        Assert.IsType<EditorSaveResult.NotFound>(await EditorSaveAsync(Editor, form.Id, File(label: "Autre")));
        Assert.IsType<EditorSaveResult.NotFound>(await EditorSaveAsync(Admin, Guid.NewGuid(), File(label: "Autre")));
        Assert.Empty(_store.Drafts);
        Assert.All(_audit.Entries, e => Assert.Equal(("access.denied", $"form:{form.Id}", "form-editor"), e));

        _backOffice.CanAccess = true;
        Assert.IsType<EditorSaveResult.Saved>(await EditorSaveAsync(Editor, form.Id, File(label: "Autre")));
    }
}
