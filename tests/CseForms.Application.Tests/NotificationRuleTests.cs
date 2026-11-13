using CseForms.Application.BackOffice;
using CseForms.Application.Notifications;
using CseForms.Application.Settings;
using CseForms.Domain.Notifications;

namespace CseForms.Application.Tests;

/// <summary>Règle de notification réglée depuis le back-office : droits, validation, aperçu, journal.</summary>
public class NotificationRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly CurrentUser Editor = new(Guid.NewGuid(), IsAdmin: false);
    private static readonly CurrentUser Reader = new(Guid.NewGuid(), IsAdmin: false, IsReadOnly: true);

    private readonly FakeBackOffice _backOffice = new();
    private readonly FakeStore _store = new();
    private readonly FakeAudit _audit = new();
    private Dictionary<string, string> _settings = new() { [SettingKeys.PublicBaseUrl] = "https://formulaires.example.test" };

    private NotificationRuleService Service() => new(_backOffice, _store, new FakeSettings(_settings), _audit, new FixedTimeProvider(Now));

    private Guid FormId => _store.Form.Id;

    [Fact]
    public async Task Recipients_are_split_normalized_saved_and_only_their_count_is_audited()
    {
        var result = await Service().SaveAsync(Editor, FormId, " Bureau@Example.test\nelu@example.test; bureau@example.test ", null, null, CancellationToken.None);

        Assert.Equal(NotificationStatus.Ok, result.Status);
        Assert.Equal(["bureau@example.test", "elu@example.test"], _store.Rule!.Recipients);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("form.notifications", $"form:{FormId}", "recipients=2"), entry);
        Assert.DoesNotContain("@", entry.Detail);
    }

    [Fact]
    public async Task Existing_rule_is_updated_in_place()
    {
        var service = Service();
        await service.SaveAsync(Editor, FormId, "bureau@example.test", null, null, CancellationToken.None);
        var first = _store.Rule!;

        await service.SaveAsync(Editor, FormId, "autre@example.test", "Dossier {numero}", "Voir {lien}", CancellationToken.None);

        Assert.Same(first, _store.Rule);
        Assert.Equal(["autre@example.test"], first.Recipients);
        Assert.Equal("Dossier {numero}", first.Subject);
        Assert.Equal(1, _store.Adds);
    }

    [Theory]
    [InlineData("", null, null, "destinataires")]
    [InlineData("pas-une-adresse", null, null, "invalide")]
    [InlineData("bureau@example.test", "Objet {nom}", null, "{nom}")]
    [InlineData("bureau@example.test", null, "Bonjour {prenom} {numero}", "{prenom}")]
    [InlineData("bureau@example.test", "Objet\nBcc: x@example.test", null, "une seule ligne")]
    public async Task Invalid_rule_is_refused_with_a_message_and_nothing_saved(string recipients, string? subject, string? body, string expected)
    {
        var result = await Service().SaveAsync(Editor, FormId, recipients, subject, body, CancellationToken.None);

        Assert.Equal(NotificationStatus.Invalid, result.Status);
        Assert.Contains(expected, result.Error);
        Assert.Null(_store.Rule);
        Assert.Equal(0, _store.Saves);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Too_many_recipients_are_refused()
    {
        var many = string.Join('\n', Enumerable.Range(1, NotificationRule.MaxRecipients + 1).Select(i => $"elu{i}@example.test"));

        var result = await Service().SaveAsync(Editor, FormId, many, null, null, CancellationToken.None);

        Assert.Equal(NotificationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Preview_is_the_real_message_on_a_fictitious_case()
    {
        var service = Service();
        await service.SaveAsync(Editor, FormId, "bureau@example.test", "Dossier {numero} ({site})", "Nouveau dossier le {date}.", CancellationToken.None);

        var page = (await service.GetPageAsync(Editor, FormId, CancellationToken.None)).Value!;

        Assert.False(page.MissingBaseUrl);
        Assert.Equal("Dossier TST-2026-0042 (Site d'exemple)", page.Preview!.Subject);
        Assert.StartsWith("Nouveau dossier le 13/11/2026.", page.Preview.Body);
        Assert.Contains("https://formulaires.example.test/admin/reponses/", page.Preview.Body);
    }

    [Fact]
    public async Task Page_warns_when_the_public_address_is_missing_and_has_no_preview_without_rule()
    {
        _settings = [];

        var page = (await Service().GetPageAsync(Editor, FormId, CancellationToken.None)).Value!;

        Assert.True(page.MissingBaseUrl);
        Assert.Null(page.Rule);
        Assert.Null(page.Preview);
    }

    [Fact]
    public async Task Removing_disables_notifications_and_is_audited_once()
    {
        var service = Service();
        await service.SaveAsync(Editor, FormId, "bureau@example.test", null, null, CancellationToken.None);
        _audit.Entries.Clear();

        Assert.Equal(NotificationStatus.Ok, (await service.RemoveAsync(Editor, FormId, CancellationToken.None)).Status);
        Assert.Null(_store.Rule);
        Assert.Equal(NotificationStatus.Ok, (await service.RemoveAsync(Editor, FormId, CancellationToken.None)).Status);

        Assert.Equal(("form.notifications.remove", $"form:{FormId}", null), Assert.Single(_audit.Entries));
    }

    [Fact]
    public async Task Reader_is_forbidden_unassigned_account_gets_not_found_both_audited()
    {
        Assert.Equal(NotificationStatus.Forbidden, (await Service().SaveAsync(Reader, FormId, "bureau@example.test", null, null, CancellationToken.None)).Status);
        Assert.Equal(NotificationStatus.Forbidden, (await Service().GetPageAsync(Reader, FormId, CancellationToken.None)).Status);

        _backOffice.CanAccess = false;
        Assert.Equal(NotificationStatus.NotFound, (await Service().RemoveAsync(Editor, FormId, CancellationToken.None)).Status);
        Assert.Equal(NotificationStatus.NotFound, (await Service().GetPageAsync(Editor, Guid.NewGuid(), CancellationToken.None)).Status);

        Assert.Null(_store.Rule);
        Assert.Equal(3, _audit.Entries.Count(e => e.Action == "access.denied"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore : INotificationRuleStore
    {
        public NotificationForm Form { get; } = new(Guid.NewGuid(), "essai", "Formulaire fictif", "TST");

        public NotificationRule? Rule { get; private set; }

        public int Saves { get; private set; }

        public int Adds { get; private set; }

        private NotificationRule? _pending;
        private bool _removed;

        public Task<NotificationForm?> GetFormAsync(Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult(formId == Form.Id ? Form : null);

        public Task<NotificationRule?> FindRuleAsync(Guid formId, CancellationToken cancellationToken) => Task.FromResult(Rule);

        public void Add(NotificationRule rule)
        {
            _pending = rule;
            Adds++;
        }

        public void Remove(NotificationRule rule) => _removed = true;

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (_pending is not null)
            {
                Rule = _pending;
                _pending = null;
            }

            if (_removed)
            {
                Rule = null;
                _removed = false;
            }

            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettings(IReadOnlyDictionary<string, string> values) : ISettingReader
    {
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(values);
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

    private sealed class FakeBackOffice : IBackOfficeStore
    {
        public bool CanAccess { get; set; } = true;

        public Task<bool> CanAccessFormAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken) => Task.FromResult(CanAccess);

        public Task<IReadOnlyList<FormSummary>> ListFormsAsync(CurrentUser user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FormHeader?> GetFormAsync(Guid formId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SubmissionPage> ListSubmissionsAsync(SubmissionQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SubmissionRecord?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PdfState> GetPdfStateAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StoredPdf?> GetPdfFileAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RequestPdfAsync(Guid submissionId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> ChangeStatusAsync(Guid submissionId, string status, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ExportData> GetExportDataAsync(SubmissionQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
