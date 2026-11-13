using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.FormAdministration;
using CseForms.Domain.Forms;
using Microsoft.Extensions.Logging.Abstractions;

namespace CseForms.Application.Tests;

/// <summary>Import d'un fichier de formulaire depuis le back-office : droits, code d'accès, passage en public, journal.</summary>
public class FormImportTests
{
    private static readonly CurrentUser Admin = new(Guid.NewGuid(), IsAdmin: true);
    private static readonly CurrentUser Editor = new(Guid.NewGuid(), IsAdmin: false);
    private static readonly CurrentUser Reader = new(Guid.NewGuid(), IsAdmin: false, IsReadOnly: true);

    private readonly FakeBackOffice _backOffice = new();
    private readonly FakeStore _store = new();
    private readonly FakeAudit _audit = new();

    private FormImportService Service() => new(
        _backOffice,
        _store,
        new FormAdminService(_store, new FakeHasher(), TimeProvider.System, NullLogger<FormAdminService>.Instance),
        _audit);

    private static string File(string mode = "public", int? retentionDays = 30, string label = "Nom", string extra = "") =>
        $$"""
        {"slug":"essai-web","title":"Formulaire fictif","access":{"mode":"{{mode}}"}{{(retentionDays is { } d ? $",\"retentionDays\":{d}" : "")}}{{extra}},
         "sections":[{"id":"s","fields":[{"id":"nom","type":"text","label":"{{label}}"}]}]}
        """;

    private Task<FormImportResult> ImportAsync(CurrentUser user, string json, string? template = null, string? code = null, string? confirmation = null, bool confirmPublic = false) =>
        Service().ImportAsync(user, new FormImportRequest(json, template, code, confirmation ?? code, confirmPublic), CancellationToken.None);

    [Fact]
    public async Task Admin_creates_a_form_and_the_import_is_audited()
    {
        var result = Assert.IsType<FormImportResult.Done>(await ImportAsync(Admin, File()));

        Assert.True(result.Created);
        Assert.Equal(1, result.Version);
        Assert.Equal("essai-web", result.Slug);
        var form = _store.Forms.Single();
        Assert.Equal($"user:{Admin.Id}", form.CurrentVersion!.CreatedBy);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("form.import", $"form:{form.Id}", "create;v1"), entry);
    }

    [Fact]
    public async Task Editor_cannot_create_a_form()
    {
        var result = Assert.IsType<FormImportResult.Invalid>(await ImportAsync(Editor, File()));

        Assert.Equal(FormImportService.NotAllowed, Assert.Single(result.Errors).Message);
        Assert.Empty(_store.Forms);
        Assert.Equal(("access.denied", null, "form-import"), Assert.Single(_audit.Entries));
    }

    [Fact]
    public async Task Editor_publishes_a_new_version_of_an_assigned_form_only_and_gets_the_same_message_otherwise()
    {
        await ImportAsync(Admin, File());
        var form = _store.Forms.Single();
        _audit.Entries.Clear();

        _backOffice.CanAccess = false;
        var refused = Assert.IsType<FormImportResult.Invalid>(await ImportAsync(Editor, File(label: "Nom de famille")));
        Assert.Equal(FormImportService.NotAllowed, Assert.Single(refused.Errors).Message);
        Assert.Equal(("access.denied", $"form:{form.Id}", "form-import"), Assert.Single(_audit.Entries));
        Assert.Single(form.Versions);

        _backOffice.CanAccess = true;
        var done = Assert.IsType<FormImportResult.Done>(await ImportAsync(Editor, File(label: "Nom de famille")));
        Assert.False(done.Created);
        Assert.Equal(2, done.Version);
        Assert.Contains(_audit.Entries, e => e == ("form.import", $"form:{form.Id}", "v2"));
    }

    [Fact]
    public async Task Unchanged_file_publishes_nothing_and_is_not_audited()
    {
        await ImportAsync(Admin, File());
        _audit.Entries.Clear();

        var result = Assert.IsType<FormImportResult.Done>(await ImportAsync(Admin, File()));

        Assert.True(result.Unchanged);
        Assert.Equal(1, result.Version);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Reader_is_forbidden_and_audited()
    {
        Assert.IsType<FormImportResult.Forbidden>(await ImportAsync(Reader, File()));

        Assert.Equal(("access.denied", null, "form-import"), Assert.Single(_audit.Entries));
    }

    [Fact]
    public async Task Invalid_file_returns_every_error_and_saves_nothing()
    {
        var result = Assert.IsType<FormImportResult.Invalid>(await ImportAsync(Admin, """{"slug":"x","inconnu":1}"""));

        Assert.Contains(result.Errors, e => e.Path == "$.inconnu");
        Assert.Contains(result.Errors, e => e.Path == "$.title");
        Assert.Equal(0, _store.Saves);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Pdf_template_is_taken_from_the_upload_and_must_be_declared()
    {
        var undeclared = Assert.IsType<FormImportResult.Invalid>(await ImportAsync(Admin, File(), template: "<p>{{ field.nom }}</p>"));
        Assert.Equal("$.pdfTemplate", Assert.Single(undeclared.Errors).Path);

        var missing = Assert.IsType<FormImportResult.Invalid>(await ImportAsync(Admin, File(extra: ",\"pdfTemplate\":\"essai.pdf.html\"")));
        Assert.Contains(missing.Errors, e => e.Path == "$.pdfTemplate");

        Assert.IsType<FormImportResult.Done>(await ImportAsync(Admin, File(extra: ",\"pdfTemplate\":\"essai.pdf.html\""), template: "<p>{{ field.nom }}</p>"));
        Assert.Equal("<p>{{ field.nom }}</p>", _store.Forms.Single().CurrentVersion!.PdfTemplateHtml);
    }

    [Fact]
    public async Task Protected_form_needs_a_twice_typed_access_code_on_first_import()
    {
        Assert.IsType<FormImportResult.AccessCodeRequired>(await ImportAsync(Admin, File(mode: "pin")));
        Assert.Empty(_store.Forms);

        var mistyped = Assert.IsType<FormImportResult.Invalid>(await ImportAsync(Admin, File(mode: "pin"), code: "482193", confirmation: "482139"));
        Assert.Contains("différentes", Assert.Single(mistyped.Errors).Message);
        Assert.Empty(_store.Forms);

        var shortCode = Assert.IsType<FormImportResult.Invalid>(await ImportAsync(Admin, File(mode: "pin"), code: "12"));
        Assert.Contains("4 à 12 chiffres", Assert.Single(shortCode.Errors).Message);
        Assert.Empty(_store.Forms);

        Assert.IsType<FormImportResult.Done>(await ImportAsync(Admin, File(mode: "pin"), code: "482193"));
        Assert.Equal("hash:482193", _store.Forms.Single().AccessCodeHash);
        Assert.Equal(["form.import", "form.access_code"], _audit.Entries.Select(e => e.Action));
    }

    [Fact]
    public async Task Making_a_protected_form_public_must_be_confirmed()
    {
        await ImportAsync(Admin, File(mode: "pin"), code: "482193");
        var form = _store.Forms.Single();
        _audit.Entries.Clear();

        Assert.IsType<FormImportResult.ConfirmPublicRequired>(await ImportAsync(Admin, File(mode: "public")));
        Assert.Equal("hash:482193", form.AccessCodeHash);
        Assert.Single(form.Versions);

        var done = Assert.IsType<FormImportResult.Done>(await ImportAsync(Admin, File(mode: "public"), confirmPublic: true));
        Assert.True(done.BecamePublic);
        Assert.Null(form.AccessCodeHash);
        Assert.Contains(_audit.Entries, e => e.Action == "form.public");
    }

    [Fact]
    public async Task Retention_change_and_closed_state_are_reported()
    {
        await ImportAsync(Admin, File(retentionDays: 180));
        var form = _store.Forms.Single();
        form.Close();

        var done = Assert.IsType<FormImportResult.Done>(await ImportAsync(Admin, File(retentionDays: 90, label: "Nom de famille")));

        Assert.Equal(new RetentionChange(180, 90), done.RetentionChange);
        Assert.True(done.StillClosed);
        Assert.Equal(FormStatus.Closed, form.Status);
    }

    private sealed class FakeStore : IFormAdminStore
    {
        public List<Form> Forms { get; } = [];

        public int Saves { get; private set; }

        private readonly List<Form> _pending = [];

        public Task<Form?> FindBySlugAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(Forms.SingleOrDefault(f => f.Slug == slug));

        public void Add(Form form) => _pending.Add(form);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Forms.AddRange(_pending);
            _pending.Clear();
            Saves++;
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
