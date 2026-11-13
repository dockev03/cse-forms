using System.Text.Json.Nodes;
using CseForms.Application.BackOffice;
using CseForms.Application.FormAdministration;
using CseForms.Domain.Forms;

namespace CseForms.Application.Tests;

/// <summary>Modèles de formulaires : catalogue livré, création d'un formulaire, enregistrement et suppression, droits.</summary>
public class FormTemplateTests
{
    private static readonly CurrentUser Admin = new(Guid.NewGuid(), IsAdmin: true);
    private static readonly CurrentUser Editor = new(Guid.NewGuid(), IsAdmin: false);

    private static readonly DateTimeOffset Past = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Schema =
        """{"title":"Sortie fictive","sections":[{"id":"s","title":"Participant","fields":[{"id":"nom","type":"text","label":"Nom"},{"id":"note","type":"info","text":"Texte fictif."}]}]}""";

    private const string PdfTemplate = """<h1>{{ form.title }}</h1><p>{{ field.nom }}</p>""";

    private readonly FakeStore _store = new();
    private readonly FakeAudit _audit = new();

    private FormTemplateService Service() => new(_store, _audit, TimeProvider.System);

    private Form PublishedForm(string slug = "sortie-2026", bool protectedByCode = false, bool publish = true, string? pdf = null)
    {
        var form = new Form(slug, "Sortie fictive 2026", Past);
        form.SetCaseNumberPrefix("SOR");
        form.SetRetentionDays(60);
        form.SetPdfWatermark(true);
        form.SetMaxResponses(40);
        if (protectedByCode)
        {
            form.SetAccessCodeHash("hash:582941");
        }

        if (publish)
        {
            form.Publish(Schema, """{"multiStep":true}""", pdf, Past, "test");
        }

        _store.Forms.Add(form);
        return form;
    }

    [Theory]
    [InlineData("inscription")]
    [InlineData("satisfaction")]
    [InlineData("evenement")]
    public void Each_built_in_template_passes_the_import_checks_and_is_public(string code)
    {
        var template = BuiltInFormTemplates.Find(code);

        Assert.NotNull(template);
        Assert.False(string.IsNullOrWhiteSpace(template.Name));
        Assert.False(string.IsNullOrWhiteSpace(template.Description));
        var read = FormImportService.ReadUpload(template.PackageJson, null);
        Assert.Empty(read.Errors);
        Assert.Equal(AccessMode.Public, read.Package!.AccessMode);
        Assert.NotNull(read.Package.RetentionDays);
    }

    [Fact]
    public void Built_in_catalog_lists_the_three_templates_and_the_satisfaction_one_asks_no_identity()
    {
        Assert.Equal(["inscription", "satisfaction", "evenement"], BuiltInFormTemplates.All.Select(t => t.Code));

        var satisfaction = FormImportService.ReadUpload(BuiltInFormTemplates.Find("satisfaction")!.PackageJson, null).Package!;
        var types = JsonNode.Parse(satisfaction.SchemaJson)!["sections"]!.AsArray()
            .SelectMany(s => s!["fields"]!.AsArray())
            .Select(f => f!["type"]!.GetValue<string>())
            .ToList();
        Assert.DoesNotContain("email", types);
        Assert.DoesNotContain("phone", types);
        Assert.DoesNotContain("address", types);
        Assert.DoesNotContain("text", types);
        Assert.Null(JsonNode.Parse(satisfaction.SchemaJson)!["receipt"]);
    }

    [Fact]
    public async Task Admin_creates_an_offline_draft_form_from_a_built_in_template()
    {
        var created = Assert.IsType<TemplateUseResult.Created>(
            await Service().CreateFormAsync(Admin, "evenement", " Noel-2026 ", "Arbre de Noël 2026", CancellationToken.None));

        Assert.False(created.NeedsAccessCode);
        var form = _store.Forms.Single(f => f.Id == created.FormId);
        Assert.Equal(("noel-2026", "Arbre de Noël 2026", FormStatus.Draft), (form.Slug, form.Title, form.Status));
        Assert.Empty(form.Versions);

        var draft = Assert.Single(_store.Drafts);
        Assert.Equal((form.Id, $"user:{Admin.Id}"), (draft.FormId, draft.SavedBy));
        var package = FormPackageReader.Read(draft.PackageJson).Package!;
        Assert.Equal(("noel-2026", "Arbre de Noël 2026", "EVT"), (package.Slug, package.Title, package.CaseNumberPrefix));
        Assert.Equal("Arbre de Noël 2026", JsonNode.Parse(package.SchemaJson)!["title"]!.GetValue<string>());
        Assert.Equal(1, _store.Saves);
        Assert.Equal(("form.template.use", $"form:{form.Id}", "template=evenement"), Assert.Single(_audit.Entries));
    }

    [Fact]
    public async Task Admin_saves_a_published_form_as_template_without_its_settings_then_uses_it()
    {
        var source = PublishedForm(protectedByCode: true, pdf: PdfTemplate);

        var saved = Assert.IsType<TemplateSaveResult.Saved>(
            await Service().SaveFromFormAsync(Admin, source.Id, "  Sortie   annuelle ", "Modèle fictif.", CancellationToken.None));

        var template = Assert.Single(_store.Templates);
        Assert.Equal((saved.TemplateId, "Sortie annuelle", "Modèle fictif.", $"user:{Admin.Id}"), (template.Id, template.Name, template.Description, template.CreatedBy));
        Assert.Equal(PdfTemplate, template.PdfTemplateHtml);
        Assert.DoesNotContain("hash", template.PackageJson);
        Assert.Equal(("form.template.save", $"template:{template.Id}", $"from=form:{source.Id};v1"), _audit.Entries[^1]);

        var list = await Service().ListAsync(Admin, CancellationToken.None);
        Assert.Equal(4, list!.Count);
        Assert.Equal((template.Id.ToString("D"), false), (list[^1].Key, list[^1].BuiltIn));

        var detail = await Service().GetAsync(Admin, template.Id.ToString(), CancellationToken.None);
        Assert.Equal((AccessMode.Pin, true, false), (detail!.AccessMode, detail.HasPdfTemplate, detail.HasReceipt));
        Assert.Equal(new TemplateSection("Participant", 1), Assert.Single(detail.Sections));

        var created = Assert.IsType<TemplateUseResult.Created>(
            await Service().CreateFormAsync(Admin, template.Id.ToString(), "sortie-2027", "Sortie fictive 2027", CancellationToken.None));
        Assert.True(created.NeedsAccessCode);
        var form = _store.Forms.Single(f => f.Id == created.FormId);
        Assert.Null(form.AccessCodeHash);
        Assert.False(form.PdfWatermark);
        Assert.Null(form.MaxResponses);
        var draft = _store.Drafts.Single(d => d.FormId == form.Id);
        Assert.Equal(PdfTemplate, draft.PdfTemplateHtml);
        Assert.Equal(("sortie-2027", 60, AccessMode.Pin), FormImportService.ReadUpload(draft.PackageJson, draft.PdfTemplateHtml).Package is { } p ? (p.Slug, p.RetentionDays!.Value, p.AccessMode) : default);
        Assert.Equal(("form.template.use", $"form:{form.Id}", $"template=template:{template.Id}"), _audit.Entries[^1]);
    }

    [Fact]
    public async Task Saving_refuses_an_unpublished_form_a_taken_name_and_an_empty_name()
    {
        var draftOnly = PublishedForm(slug: "brouillon", publish: false);
        Assert.Equal(FormTemplateService.NotPublished, SaveError(await Service().SaveFromFormAsync(Admin, draftOnly.Id, "Nom fictif", null, CancellationToken.None)));

        var source = PublishedForm();
        Assert.Equal(FormTemplateService.NameTaken, SaveError(await Service().SaveFromFormAsync(Admin, source.Id, "questionnaire DE satisfaction", null, CancellationToken.None)));
        Assert.Contains("nom", SaveError(await Service().SaveFromFormAsync(Admin, source.Id, "   ", null, CancellationToken.None)));
        Assert.Contains("description", SaveError(await Service().SaveFromFormAsync(Admin, source.Id, "Nom fictif", new string('a', 501), CancellationToken.None)));
        Assert.IsType<TemplateSaveResult.NotFound>(await Service().SaveFromFormAsync(Admin, Guid.NewGuid(), "Nom fictif", null, CancellationToken.None));

        Assert.Empty(_store.Templates);
        Assert.Equal(0, _store.Saves);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Creation_refuses_a_taken_or_invalid_address_an_empty_title_and_an_unknown_template()
    {
        PublishedForm(slug: "pris");

        Assert.Equal(FormDuplicationService.SlugTaken, UseError(await Service().CreateFormAsync(Admin, "satisfaction", "PRIS", "Titre fictif", CancellationToken.None)));
        Assert.Contains("slug", UseError(await Service().CreateFormAsync(Admin, "satisfaction", "avis client", "Titre fictif", CancellationToken.None)));
        Assert.Contains("titre", UseError(await Service().CreateFormAsync(Admin, "satisfaction", "avis", " ", CancellationToken.None)));
        Assert.IsType<TemplateUseResult.NotFound>(await Service().CreateFormAsync(Admin, "inconnu", "avis", "Titre fictif", CancellationToken.None));
        Assert.IsType<TemplateUseResult.NotFound>(await Service().CreateFormAsync(Admin, Guid.NewGuid().ToString(), "avis", "Titre fictif", CancellationToken.None));
        Assert.Null(await Service().GetAsync(Admin, "inconnu", CancellationToken.None));

        Assert.Empty(_store.Drafts);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task Admin_deletes_a_saved_template_and_forms_created_from_it_are_kept()
    {
        var source = PublishedForm();
        var saved = Assert.IsType<TemplateSaveResult.Saved>(await Service().SaveFromFormAsync(Admin, source.Id, "Sortie", null, CancellationToken.None));
        var created = Assert.IsType<TemplateUseResult.Created>(
            await Service().CreateFormAsync(Admin, saved.TemplateId.ToString(), "sortie-bis", "Sortie bis", CancellationToken.None));

        Assert.Equal(LifecycleStatus.Ok, await Service().DeleteAsync(Admin, saved.TemplateId, CancellationToken.None));

        Assert.Empty(_store.Templates);
        Assert.Contains(_store.Forms, f => f.Id == created.FormId);
        Assert.Contains(_store.Drafts, d => d.FormId == created.FormId);
        Assert.Equal(("form.template.delete", $"template:{saved.TemplateId}", null), _audit.Entries[^1]);
        Assert.Equal(LifecycleStatus.NotFound, await Service().DeleteAsync(Admin, saved.TemplateId, CancellationToken.None));
    }

    [Fact]
    public async Task Every_template_action_is_reserved_to_admins_and_refusals_are_audited()
    {
        var source = PublishedForm();
        var template = new FormTemplate("Fictif", null, BuiltInFormTemplates.Find("satisfaction")!.PackageJson, null, Past, "test");
        _store.Templates.Add(template);
        var service = Service();

        Assert.Null(await service.ListAsync(Editor, CancellationToken.None));
        Assert.Null(await service.GetAsync(Editor, "satisfaction", CancellationToken.None));
        Assert.IsType<TemplateUseResult.Forbidden>(await service.CreateFormAsync(Editor, "satisfaction", "avis", "Avis", CancellationToken.None));
        Assert.Null(await service.GetSaveSourceAsync(Editor, source.Id, CancellationToken.None));
        Assert.IsType<TemplateSaveResult.Forbidden>(await service.SaveFromFormAsync(Editor, source.Id, "Nom", null, CancellationToken.None));
        Assert.Null(await service.GetSavedAsync(Editor, template.Id, CancellationToken.None));
        Assert.Equal(LifecycleStatus.Forbidden, await service.DeleteAsync(Editor, template.Id, CancellationToken.None));

        Assert.Single(_store.Forms);
        Assert.Single(_store.Templates);
        Assert.Empty(_store.Drafts);
        Assert.Equal(7, _audit.Entries.Count);
        Assert.All(_audit.Entries, e => Assert.Equal(("access.denied", "form-template"), (e.Action, e.Detail)));
        Assert.Equal(
            [null, null, null, $"form:{source.Id}", $"form:{source.Id}", $"template:{template.Id}", $"template:{template.Id}"],
            _audit.Entries.Select(e => e.Target));
    }

    [Fact]
    public void Rename_replaces_address_title_and_pdf_template_name()
    {
        var json = FormTemplateService.Rename("""{"slug":"a","title":"A","pdfTemplate":"a.pdf.html","access":{"mode":"public"}}""", "b", "B")!;

        var node = JsonNode.Parse(json)!;
        Assert.Equal(("b", "B", "b.pdf.html"), (node["slug"]!.GetValue<string>(), node["title"]!.GetValue<string>(), node["pdfTemplate"]!.GetValue<string>()));
        Assert.Null(FormTemplateService.Rename("[]", "b", "B"));
        Assert.Null(FormTemplateService.Rename("{", "b", "B"));
    }

    private static string SaveError(TemplateSaveResult result) => Assert.Single(Assert.IsType<TemplateSaveResult.Invalid>(result).Errors).Message;

    private static string UseError(TemplateUseResult result) => Assert.Single(Assert.IsType<TemplateUseResult.Invalid>(result).Errors).Message;

    private sealed class FakeStore : IFormTemplateStore
    {
        public List<Form> Forms { get; } = [];

        public List<FormDraft> Drafts { get; } = [];

        public List<FormTemplate> Templates { get; } = [];

        public int Saves { get; private set; }

        public Task<IReadOnlyList<FormTemplate>> ListTemplatesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FormTemplate>>([.. Templates]);

        public Task<FormTemplate?> FindTemplateAsync(Guid templateId, CancellationToken cancellationToken) =>
            Task.FromResult(Templates.SingleOrDefault(t => t.Id == templateId));

        public Task<Form?> FindFormAsync(Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult(Forms.SingleOrDefault(f => f.Id == formId));

        public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(Forms.Any(f => f.Slug == slug));

        public void AddForm(Form form) => Forms.Add(form);

        public void AddDraft(FormDraft draft) => Drafts.Add(draft);

        public void AddTemplate(FormTemplate template) => Templates.Add(template);

        public void RemoveTemplate(FormTemplate template) => Templates.Remove(template);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAudit : IAuditLog
    {
        public List<(string Action, string? Target, string? Detail)> Entries { get; } = [];

        public Task RecordAsync(string action, Guid? userId, string? target, string? detail, CancellationToken cancellationToken)
        {
            Entries.Add((action, target, detail));
            return Task.CompletedTask;
        }
    }
}
