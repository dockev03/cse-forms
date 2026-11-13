using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.FormAdministration;
using CseForms.Domain.Forms;
using Microsoft.Extensions.Logging.Abstractions;

namespace CseForms.Application.Tests;

/// <summary>Brouillon d'un formulaire : enregistrement sans publication, aperçu, publication, suppression, droits.</summary>
public partial class FormDraftTests
{
    private static readonly CurrentUser Admin = new(Guid.NewGuid(), IsAdmin: true);
    private static readonly CurrentUser Editor = new(Guid.NewGuid(), IsAdmin: false);
    private static readonly CurrentUser Reader = new(Guid.NewGuid(), IsAdmin: false, IsReadOnly: true);

    private readonly FakeBackOffice _backOffice = new();
    private readonly FakeStore _store = new();
    private readonly FakeAudit _audit = new();

    private FormDraftService Service()
    {
        var admin = new FormAdminService(_store, new FakeHasher(), TimeProvider.System, NullLogger<FormAdminService>.Instance);
        var import = new FormImportService(_backOffice, _store, admin, _audit);
        return new FormDraftService(_backOffice, _store, import, _audit, TimeProvider.System);
    }

    private static string File(string mode = "public", string label = "Nom") =>
        $$"""
        {"slug":"essai-brouillon","title":"Formulaire fictif","access":{"mode":"{{mode}}"},"retentionDays":30,
         "ui":{"multiStep":false},
         "sections":[{"id":"s","fields":[{"id":"nom","type":"text","label":"{{label}}"},{"id":"site","type":"site","label":"Site"}]}]}
        """;

    private Task<DraftSaveResult> SaveAsync(CurrentUser user, string json, string? template = null) =>
        Service().SaveAsync(user, json, template, CancellationToken.None);

    private async Task<Form> PublishedFormAsync(string mode = "public", string? code = null)
    {
        var import = new FormImportService(
            _backOffice,
            _store,
            new FormAdminService(_store, new FakeHasher(), TimeProvider.System, NullLogger<FormAdminService>.Instance),
            _audit);
        Assert.IsType<FormImportResult.Done>(await import.ImportAsync(Admin, new FormImportRequest(File(mode), AccessCode: code, AccessCodeConfirmation: code), CancellationToken.None));
        _audit.Entries.Clear();
        return _store.Forms.Single();
    }

    [Fact]
    public async Task Admin_saves_a_draft_of_a_new_form_which_stays_offline_without_version()
    {
        var saved = Assert.IsType<DraftSaveResult.Saved>(await SaveAsync(Admin, File()));

        Assert.True(saved.CreatedForm);
        var form = _store.Forms.Single();
        Assert.Equal(FormStatus.Draft, form.Status);
        Assert.Empty(form.Versions);
        Assert.Null(form.CurrentVersionId);
        var draft = Assert.Single(_store.Drafts);
        Assert.Equal(form.Id, draft.FormId);
        Assert.Equal($"user:{Admin.Id}", draft.SavedBy);
        Assert.Equal(("form.draft.save", $"form:{form.Id}", "create"), Assert.Single(_audit.Entries));
    }

    [Fact]
    public async Task Draft_of_a_published_form_publishes_nothing_and_replaces_the_previous_draft()
    {
        var form = await PublishedFormAsync();

        await SaveAsync(Admin, File(label: "Nom de famille"));
        Assert.IsType<DraftSaveResult.Saved>(await SaveAsync(Admin, File(label: "Nom d'usage")));

        Assert.Single(form.Versions);
        Assert.Equal(FormStatus.Published, form.Status);
        Assert.Contains("Nom d'usage", Assert.Single(_store.Drafts).PackageJson);
        Assert.All(_audit.Entries, e => Assert.Equal(("form.draft.save", $"form:{form.Id}", null), e));
    }

    [Fact]
    public async Task Invalid_file_is_refused_and_nothing_is_saved()
    {
        var result = Assert.IsType<DraftSaveResult.Invalid>(await SaveAsync(Admin, """{"slug":"essai-brouillon","inconnu":1}"""));

        Assert.NotEmpty(result.Errors);
        Assert.Empty(_store.Forms);
        Assert.Empty(_store.Drafts);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Editor_cannot_create_a_form_by_draft_nor_draft_an_unassigned_form()
    {
        var creation = Assert.IsType<DraftSaveResult.Invalid>(await SaveAsync(Editor, File()));
        Assert.Equal(FormImportService.NotAllowed, Assert.Single(creation.Errors).Message);
        Assert.Empty(_store.Forms);

        var form = await PublishedFormAsync();
        _backOffice.CanAccess = false;
        var refused = Assert.IsType<DraftSaveResult.Invalid>(await SaveAsync(Editor, File(label: "Autre")));
        Assert.Equal(FormImportService.NotAllowed, Assert.Single(refused.Errors).Message);
        Assert.Empty(_store.Drafts);
        Assert.Equal(("access.denied", $"form:{form.Id}", "form-draft"), Assert.Single(_audit.Entries));

        _backOffice.CanAccess = true;
        Assert.IsType<DraftSaveResult.Saved>(await SaveAsync(Editor, File(label: "Autre")));
    }

    [Fact]
    public async Task Reader_can_neither_save_nor_preview_a_draft()
    {
        Assert.IsType<DraftSaveResult.Forbidden>(await SaveAsync(Reader, File()));

        await SaveAsync(Admin, File());
        var form = _store.Forms.Single();
        _audit.Entries.Clear();

        var preview = await Service().GetPreviewAsync(Reader, form.Id, CancellationToken.None);
        Assert.Equal(LifecycleStatus.Forbidden, preview.Status);
        Assert.Equal(LifecycleStatus.Forbidden, (await Service().PublishAsync(Reader, form.Id, null, null, false, CancellationToken.None)).Status);
        Assert.Equal(LifecycleStatus.Forbidden, (await Service().DeleteAsync(Reader, form.Id, CancellationToken.None)).Status);
        Assert.Equal(3, _audit.Entries.Count(e => e.Action == "access.denied"));
        Assert.Single(_store.Drafts);
    }

    [Fact]
    public async Task Unassigned_editor_gets_not_found_on_preview()
    {
        await SaveAsync(Admin, File());
        _backOffice.CanAccess = false;

        var preview = await Service().GetPreviewAsync(Editor, _store.Forms.Single().Id, CancellationToken.None);

        Assert.Equal(LifecycleStatus.NotFound, preview.Status);
        Assert.Equal(LifecycleStatus.NotFound, (await Service().GetPreviewAsync(Admin, Guid.NewGuid(), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Preview_returns_the_draft_definition_and_sorted_active_sites_not_the_published_one()
    {
        var form = await PublishedFormAsync();
        await SaveAsync(Admin, File(label: "Nom de famille"));
        _store.Sites.AddRange(["Usine Sud", "atelier Nord"]);

        var preview = (await Service().GetPreviewAsync(Admin, form.Id, CancellationToken.None)).Value!;

        Assert.Contains("Nom de famille", preview.DefinitionJson);
        Assert.Contains("multiStep", preview.UiJson);
        Assert.Equal(1, preview.PublishedVersion);
        Assert.Equal(["atelier Nord", "Usine Sud"], preview.Sites);
        Assert.False(preview.RequiresAccessCode);
        Assert.Empty(preview.Errors);
    }

    [Fact]
    public async Task Preview_without_draft_is_empty()
    {
        var form = await PublishedFormAsync();

        var preview = await Service().GetPreviewAsync(Admin, form.Id, CancellationToken.None);

        Assert.Equal(LifecycleStatus.Ok, preview.Status);
        Assert.Null(preview.Value);
    }

    [Fact]
    public async Task Publishing_a_draft_creates_the_next_version_removes_the_draft_and_is_audited()
    {
        var form = await PublishedFormAsync();
        await SaveAsync(Admin, File(label: "Nom de famille"));

        var result = await Service().PublishAsync(Editor, form.Id, null, null, false, CancellationToken.None);

        var done = Assert.IsType<FormImportResult.Done>(result.Import);
        Assert.Equal(2, done.Version);
        Assert.Equal(2, form.CurrentVersion!.Number);
        Assert.Contains("Nom de famille", form.CurrentVersion.SchemaJson);
        Assert.Equal($"user:{Editor.Id}", form.CurrentVersion.CreatedBy);
        Assert.Empty(_store.Drafts);
        Assert.Contains(_audit.Entries, e => e == ("form.import", $"form:{form.Id}", "v2;draft"));
    }

    [Fact]
    public async Task First_publication_of_a_new_form_draft_puts_it_online_in_version_1()
    {
        await SaveAsync(Admin, File());
        var form = _store.Forms.Single();

        var done = Assert.IsType<FormImportResult.Done>((await Service().PublishAsync(Admin, form.Id, null, null, false, CancellationToken.None)).Import);

        Assert.Equal(1, done.Version);
        Assert.Null(done.RetentionChange);
        Assert.Equal(FormStatus.Published, form.Status);
        Assert.Equal(30, form.RetentionDays);
        Assert.Empty(_store.Drafts);
    }

    [Fact]
    public async Task Draft_identical_to_the_online_version_publishes_nothing_but_is_removed()
    {
        var form = await PublishedFormAsync();
        await SaveAsync(Admin, File());

        var done = Assert.IsType<FormImportResult.Done>((await Service().PublishAsync(Admin, form.Id, null, null, false, CancellationToken.None)).Import);

        Assert.True(done.Unchanged);
        Assert.Single(form.Versions);
        Assert.Empty(_store.Drafts);
    }

    [Fact]
    public async Task Protected_new_form_needs_the_access_code_at_publication_and_keeps_the_draft_until_then()
    {
        await SaveAsync(Admin, File(mode: "pin"));
        var form = _store.Forms.Single();
        var preview = (await Service().GetPreviewAsync(Admin, form.Id, CancellationToken.None)).Value!;
        Assert.True(preview.RequiresAccessCode);
        Assert.True(preview.NeedsAccessCode);

        var refused = await Service().PublishAsync(Admin, form.Id, null, null, false, CancellationToken.None);
        Assert.IsType<FormImportResult.AccessCodeRequired>(refused.Import);
        Assert.Single(_store.Drafts);
        Assert.Empty(form.Versions);

        var done = await Service().PublishAsync(Admin, form.Id, "582941", "582941", false, CancellationToken.None);
        Assert.IsType<FormImportResult.Done>(done.Import);
        Assert.Equal("hash:582941", form.AccessCodeHash);
        Assert.Empty(_store.Drafts);
    }

    [Fact]
    public async Task Draft_making_a_protected_form_public_must_be_confirmed_at_publication()
    {
        var form = await PublishedFormAsync(mode: "pin", code: "582941");
        await SaveAsync(Admin, File(mode: "public", label: "Nom de famille"));
        Assert.True((await Service().GetPreviewAsync(Admin, form.Id, CancellationToken.None)).Value!.MakesPublic);

        var refused = await Service().PublishAsync(Admin, form.Id, null, null, confirmPublic: false, CancellationToken.None);
        Assert.IsType<FormImportResult.ConfirmPublicRequired>(refused.Import);
        Assert.NotNull(form.AccessCodeHash);
        Assert.Single(_store.Drafts);

        Assert.IsType<FormImportResult.Done>((await Service().PublishAsync(Admin, form.Id, null, null, confirmPublic: true, CancellationToken.None)).Import);
        Assert.Null(form.AccessCodeHash);
        Assert.Contains(_audit.Entries, e => e.Action == "form.public");
    }

    [Fact]
    public async Task Publishing_without_draft_is_not_found()
    {
        var form = await PublishedFormAsync();

        var result = await Service().PublishAsync(Admin, form.Id, null, null, false, CancellationToken.None);

        Assert.Equal(LifecycleStatus.NotFound, result.Status);
        Assert.Single(form.Versions);
    }

    [Fact]
    public async Task Deleting_a_draft_keeps_the_online_version_and_is_audited()
    {
        var form = await PublishedFormAsync();
        await SaveAsync(Admin, File(label: "Nom de famille"));
        _audit.Entries.Clear();

        Assert.Equal(LifecycleStatus.Ok, (await Service().DeleteAsync(Editor, form.Id, CancellationToken.None)).Status);

        Assert.Empty(_store.Drafts);
        Assert.Single(form.Versions);
        Assert.Equal(FormStatus.Published, form.Status);
        Assert.Equal(("form.draft.delete", $"form:{form.Id}", null), Assert.Single(_audit.Entries));
    }

    private sealed class FakeStore : IFormAdminStore, IFormDraftStore
    {
        private readonly List<Form> _pendingForms = [];
        private readonly List<FormDraft> _pendingDrafts = [];

        public List<Form> Forms { get; } = [];

        public List<FormDraft> Drafts { get; } = [];

        public List<string> Sites { get; } = [];

        public Task<Form?> FindBySlugAsync(string slug, CancellationToken cancellationToken) => FindFormBySlugAsync(slug, cancellationToken);

        public Task<Form?> FindFormBySlugAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(Forms.SingleOrDefault(f => f.Slug == slug));

        public Task<Form?> FindFormAsync(Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult(Forms.SingleOrDefault(f => f.Id == formId));

        public Task<FormDraft?> FindDraftAsync(Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult(Drafts.SingleOrDefault(d => d.FormId == formId));

        public Task<IReadOnlyCollection<string>> GetActiveSiteLabelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(Sites);

        public void Add(Form form) => _pendingForms.Add(form);

        public void AddForm(Form form) => _pendingForms.Add(form);

        public void AddDraft(FormDraft draft) => _pendingDrafts.Add(draft);

        public void RemoveDraft(FormDraft draft) => Drafts.Remove(draft);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Forms.AddRange(_pendingForms);
            Drafts.AddRange(_pendingDrafts);
            _pendingForms.Clear();
            _pendingDrafts.Clear();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FormListItem>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class FakeHasher : IAccessCodeHasher
    {
        public string Hash(string accessCode) => $"hash:{accessCode}";

        public bool Verify(string hash, string accessCode) => hash == $"hash:{accessCode}";
    }

    private sealed class FakeBackOffice : IBackOfficeStore
    {
        public bool CanAccess { get; set; } = true;

        public Task<bool> CanAccessFormAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult(user.IsAdmin || CanAccess);

        public Task<IReadOnlyList<FormSummary>> ListFormsAsync(CurrentUser user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FormHeader?> GetFormAsync(Guid formId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SubmissionPage> ListSubmissionsAsync(SubmissionQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SubmissionRecord?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PdfState> GetPdfStateAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StoredPdf?> GetPdfFileAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> GetUserDisplayNameAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RequestPdfAsync(Guid submissionId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> ChangeStatusAsync(Guid submissionId, string status, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ExportData> GetExportDataAsync(SubmissionQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
