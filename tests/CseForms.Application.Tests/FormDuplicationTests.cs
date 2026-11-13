using System.Text.Json.Nodes;
using CseForms.Application.BackOffice;
using CseForms.Application.FormAdministration;
using CseForms.Domain.Forms;
using CseForms.Domain.Notifications;

namespace CseForms.Application.Tests;

/// <summary>Duplication d'un formulaire : fichier reconstruit, brouillon hors ligne, réglages recopiés, droits.</summary>
public class FormDuplicationTests
{
    private static readonly CurrentUser Admin = new(Guid.NewGuid(), IsAdmin: true);
    private static readonly CurrentUser Editor = new(Guid.NewGuid(), IsAdmin: false);

    private static readonly DateTimeOffset Past = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Schema =
        """{"title":"Arbre de Noël fictif","receipt":{"emailField":"email","message":"Merci."},"sections":[{"id":"s","fields":[{"id":"nom","type":"text","label":"Nom"},{"id":"email","type":"email","label":"Email"}]}]}""";

    private readonly FakeStore _store = new();
    private readonly FakeAudit _audit = new();

    private FormDuplicationService Service() => new(_store, _audit, TimeProvider.System);

    private Form Source(bool protectedByCode = false, bool publish = true)
    {
        var form = new Form("noel-2025", "Arbre de Noël 2025", Past);
        form.SetCaseNumberPrefix("NOE");
        form.SetRetentionDays(90);
        form.SetPdfWatermark(true);
        form.SetSchedule(Past, Past.AddDays(30));
        form.SetMaxResponses(150);
        if (protectedByCode)
        {
            form.SetAccessCodeHash("hash:582941");
        }

        if (publish)
        {
            form.Publish(Schema, """{"multiStep":true}""", null, Past, "test");
        }

        _store.Forms.Add(form);
        return form;
    }

    private Task<DuplicationResult> DuplicateAsync(CurrentUser user, Guid sourceId, string slug = "noel-2026", string title = "Arbre de Noël 2026") =>
        Service().DuplicateAsync(user, sourceId, slug, title, CancellationToken.None);

    [Fact]
    public async Task Admin_duplicates_the_online_version_into_an_offline_draft_with_the_new_slug_and_title()
    {
        var source = Source();
        source.Publish(Schema.Replace("\"Nom\"", "\"Nom de famille\""), null, null, Past, "test");

        var created = Assert.IsType<DuplicationResult.Created>(await DuplicateAsync(Admin, source.Id));

        Assert.Equal(2, created.SourceVersion);
        var copy = _store.Forms.Single(f => f.Id == created.FormId);
        Assert.Equal(("noel-2026", "Arbre de Noël 2026"), (copy.Slug, copy.Title));
        Assert.Equal(FormStatus.Draft, copy.Status);
        Assert.Empty(copy.Versions);
        Assert.True(copy.PdfWatermark);
        Assert.Null(copy.OpensAt);
        Assert.Null(copy.ClosesAt);
        Assert.Null(copy.MaxResponses);

        var draft = Assert.Single(_store.Drafts);
        Assert.Equal(copy.Id, draft.FormId);
        Assert.Equal($"user:{Admin.Id}", draft.SavedBy);
        var package = FormPackageReader.Read(draft.PackageJson).Package!;
        Assert.Equal(("noel-2026", "Arbre de Noël 2026", "NOE", 90, AccessMode.Public), (package.Slug, package.Title, package.CaseNumberPrefix, package.RetentionDays, package.AccessMode));
        Assert.Contains("Nom de famille", package.SchemaJson);
        Assert.Equal("Arbre de Noël 2026", JsonNode.Parse(package.SchemaJson)!["title"]!.GetValue<string>());
        Assert.Contains("emailField", package.SchemaJson);
        Assert.Null(package.UiJson);

        // La source ne change pas.
        Assert.Equal(2, source.Versions.Count);
        Assert.Equal("noel-2025", source.Slug);
        Assert.Equal(1, _store.Saves);
    }

    [Fact]
    public async Task Notifications_and_assigned_accounts_are_copied_and_the_duplication_is_audited()
    {
        var source = Source();
        _store.Rules.Add(new NotificationRule(source.Id, ["elus@example.test"], "Objet {numero}", "Corps {lien}"));
        _store.Access[source.Id] = [Guid.NewGuid(), Guid.NewGuid()];

        var created = Assert.IsType<DuplicationResult.Created>(await DuplicateAsync(Admin, source.Id));

        var rule = _store.Rules.Single(r => r.FormId == created.FormId);
        Assert.Equal(["elus@example.test"], rule.Recipients);
        Assert.Equal(("Objet {numero}", "Corps {lien}"), (rule.Subject, rule.Body));
        Assert.Equal(_store.Access[source.Id], _store.Access[created.FormId]);
        Assert.Equal(2, created.GrantedAccounts);
        Assert.Equal(("form.duplicate", $"form:{created.FormId}", $"from=form:{source.Id};v1;access=2"), Assert.Single(_audit.Entries));
    }

    [Fact]
    public async Task Protected_source_gives_a_pin_draft_without_its_access_code()
    {
        var source = Source(protectedByCode: true);

        var created = Assert.IsType<DuplicationResult.Created>(await DuplicateAsync(Admin, source.Id));

        Assert.True(created.NeedsAccessCode);
        var copy = _store.Forms.Single(f => f.Id == created.FormId);
        Assert.Null(copy.AccessCodeHash);
        Assert.Equal(AccessMode.Pin, FormPackageReader.Read(_store.Drafts.Single().PackageJson).Package!.AccessMode);
    }

    [Fact]
    public async Task Editor_cannot_duplicate_and_the_refusal_is_audited()
    {
        var source = Source();

        Assert.IsType<DuplicationResult.Forbidden>(await DuplicateAsync(Editor, source.Id));
        Assert.Null(await Service().GetSourceAsync(Editor, source.Id, CancellationToken.None));

        Assert.Single(_store.Forms);
        Assert.All(_audit.Entries, e => Assert.Equal(("access.denied", $"form:{source.Id}", "form-duplicate"), e));
        Assert.Equal(2, _audit.Entries.Count);
    }

    [Fact]
    public async Task Unpublished_source_unknown_source_taken_or_invalid_slug_and_empty_title_are_refused()
    {
        var draftOnly = Source(publish: false);
        Assert.Equal(FormDuplicationService.NotPublished, Error(await DuplicateAsync(Admin, draftOnly.Id)));

        Assert.IsType<DuplicationResult.NotFound>(await DuplicateAsync(Admin, Guid.NewGuid()));

        var source = Source();
        Assert.Equal(FormDuplicationService.SlugTaken, Error(await DuplicateAsync(Admin, source.Id, slug: "NOEL-2025 ")));
        Assert.Contains("slug", Error(await DuplicateAsync(Admin, source.Id, slug: "noël 2026")));
        Assert.Contains("titre", Error(await DuplicateAsync(Admin, source.Id, title: "  ")));

        Assert.Empty(_store.Drafts);
        Assert.Equal(0, _store.Saves);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public void Rebuilt_file_of_the_real_registration_form_reads_back_identical_with_its_pdf_template()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "forms");
        var original = FormPackageReader.Read(
            File.ReadAllText(Path.Combine(root, "inscription.json")),
            name => File.ReadAllText(Path.Combine(root, name))).Package!;
        var form = new Form(original.Slug, original.Title, Past);
        form.SetCaseNumberPrefix(original.CaseNumberPrefix!);
        form.SetRetentionDays(original.RetentionDays);
        form.SetAccessCodeHash(original.AccessMode == AccessMode.Pin ? "hash" : null);
        var version = form.Publish(original.SchemaJson, original.UiJson, original.PdfTemplateHtml, Past, "test");

        var json = FormDuplicationService.BuildPackage(form, version, original.Slug, original.Title);
        var copy = FormImportService.ReadUpload(json, version.PdfTemplateHtml);

        Assert.Empty(copy.Errors);
        var package = copy.Package!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original.SchemaJson), JsonNode.Parse(package.SchemaJson)));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original.UiJson!), JsonNode.Parse(package.UiJson!)));
        Assert.Equal(original.PdfTemplateHtml, package.PdfTemplateHtml);
        Assert.Equal((original.CaseNumberPrefix, original.RetentionDays, original.AccessMode), (package.CaseNumberPrefix, package.RetentionDays, package.AccessMode));
    }

    private static string Error(DuplicationResult result) => Assert.Single(Assert.IsType<DuplicationResult.Invalid>(result).Errors).Message;

    private sealed class FakeStore : IFormDuplicationStore
    {
        public List<Form> Forms { get; } = [];

        public List<FormDraft> Drafts { get; } = [];

        public List<NotificationRule> Rules { get; } = [];

        public Dictionary<Guid, List<Guid>> Access { get; } = [];

        public int Saves { get; private set; }

        public Task<Form?> FindFormAsync(Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult(Forms.SingleOrDefault(f => f.Id == formId));

        public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(Forms.Any(f => f.Slug == slug));

        public Task<NotificationRule?> FindNotificationRuleAsync(Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult(Rules.SingleOrDefault(r => r.FormId == formId));

        public Task<int> CopyFormAccessAsync(Guid sourceFormId, Guid targetFormId, CancellationToken cancellationToken)
        {
            var users = Access.GetValueOrDefault(sourceFormId) ?? [];
            Access[targetFormId] = [.. users];
            return Task.FromResult(users.Count);
        }

        public void AddForm(Form form) => Forms.Add(form);

        public void AddDraft(FormDraft draft) => Drafts.Add(draft);

        public void AddNotificationRule(NotificationRule rule) => Rules.Add(rule);

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
