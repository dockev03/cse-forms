using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.FormAdministration;
using CseForms.Domain.Forms;

namespace CseForms.Application.Tests;

/// <summary>Cycle de vie d'un formulaire depuis le back-office : droits, règles, journal.</summary>
public class FormLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly CurrentUser Editor = new(Guid.NewGuid(), IsAdmin: false);
    private static readonly CurrentUser Reader = new(Guid.NewGuid(), IsAdmin: false, IsReadOnly: true);

    private readonly FakeBackOffice _backOffice = new();
    private readonly FakeStore _store = new();
    private readonly FakeAudit _audit = new();

    private FormLifecycleService Service() => new(_backOffice, _store, _audit, new FakeHasher(), new FixedTimeProvider(Now));

    private static Form PublishedForm(string? accessCodeHash = null)
    {
        var form = new Form("essai", "Formulaire fictif", Now.AddDays(-10));
        form.Publish("""{"sections":[]}""", null, null, Now.AddDays(-10), "test");
        form.SetAccessCodeHash(accessCodeHash);
        return form;
    }

    [Fact]
    public async Task Page_shows_state_counts_and_whether_a_code_is_set()
    {
        var form = PublishedForm("hash:1234");
        form.SetMaxResponses(10);
        _store.Form = form;
        _store.ResponseCount = 3;

        var result = await Service().GetPageAsync(Editor, form.Id, CancellationToken.None);

        Assert.Equal(LifecycleStatus.Ok, result.Status);
        var page = result.Value!;
        Assert.Equal(1, page.VersionNumber);
        Assert.Equal(3, page.ResponseCount);
        Assert.Equal(10, page.MaxResponses);
        Assert.True(page.RequiresAccessCode);
        Assert.Equal(FormAvailability.Open, page.Availability);
    }

    [Fact]
    public async Task Editor_closes_then_reopens_and_both_are_audited()
    {
        _store.Form = PublishedForm();
        var service = Service();

        Assert.Equal(LifecycleStatus.Ok, (await service.CloseAsync(Editor, _store.Form.Id, CancellationToken.None)).Status);
        Assert.Equal(FormStatus.Closed, _store.Form.Status);
        Assert.Equal(LifecycleStatus.Ok, (await service.ReopenAsync(Editor, _store.Form.Id, CancellationToken.None)).Status);
        Assert.Equal(FormStatus.Published, _store.Form.Status);

        Assert.Equal(2, _store.Saves);
        Assert.Equal(["form.close", "form.reopen"], _audit.Entries.Select(e => e.Action));
        Assert.All(_audit.Entries, e => Assert.Equal($"form:{_store.Form.Id}", e.Target));
    }

    [Fact]
    public async Task Watermark_is_switched_on_and_off_audited_once_per_change()
    {
        _store.Form = PublishedForm();
        var service = Service();

        Assert.False((await service.GetPageAsync(Editor, _store.Form.Id, CancellationToken.None)).Value!.PdfWatermark);
        Assert.Equal(LifecycleStatus.Ok, (await service.SetPdfWatermarkAsync(Editor, _store.Form.Id, enabled: true, CancellationToken.None)).Status);
        Assert.True(_store.Form.PdfWatermark);
        Assert.True((await service.GetPageAsync(Editor, _store.Form.Id, CancellationToken.None)).Value!.PdfWatermark);
        await service.SetPdfWatermarkAsync(Editor, _store.Form.Id, enabled: true, CancellationToken.None);
        await service.SetPdfWatermarkAsync(Editor, _store.Form.Id, enabled: false, CancellationToken.None);

        Assert.False(_store.Form.PdfWatermark);
        Assert.Equal(2, _store.Saves);
        Assert.Equal(["on", "off"], _audit.Entries.Where(e => e.Action == "form.pdf_watermark").Select(e => e.Detail));
    }

    [Fact]
    public async Task Reader_cannot_change_the_watermark()
    {
        _store.Form = PublishedForm();

        Assert.Equal(LifecycleStatus.Forbidden, (await Service().SetPdfWatermarkAsync(Reader, _store.Form.Id, enabled: true, CancellationToken.None)).Status);
        Assert.False(_store.Form.PdfWatermark);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task Closing_twice_is_refused_with_a_message_and_nothing_saved()
    {
        _store.Form = PublishedForm();
        _store.Form.Close();

        var result = await Service().CloseAsync(Editor, _store.Form.Id, CancellationToken.None);

        Assert.Equal(LifecycleStatus.Invalid, result.Status);
        Assert.Contains("publié", result.Error);
        Assert.Equal(0, _store.Saves);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Reader_is_forbidden_and_unassigned_account_gets_not_found_both_audited()
    {
        _store.Form = PublishedForm();

        Assert.Equal(LifecycleStatus.Forbidden, (await Service().CloseAsync(Reader, _store.Form.Id, CancellationToken.None)).Status);
        Assert.Equal(LifecycleStatus.Forbidden, (await Service().GetPageAsync(Reader, _store.Form.Id, CancellationToken.None)).Status);

        _backOffice.CanAccess = false;
        Assert.Equal(LifecycleStatus.NotFound, (await Service().CloseAsync(Editor, _store.Form.Id, CancellationToken.None)).Status);

        Assert.Equal(FormStatus.Published, _store.Form.Status);
        Assert.Equal(0, _store.Saves);
        Assert.Equal(3, _audit.Entries.Count(e => e.Action == "access.denied"));
    }

    [Fact]
    public async Task Unknown_form_is_not_found_without_audit()
    {
        var result = await Service().GetPageAsync(Editor, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(LifecycleStatus.NotFound, result.Status);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Schedule_is_read_in_paris_time_stored_in_utc_and_audited_without_personal_data()
    {
        _store.Form = PublishedForm();

        var result = await Service().UpdateScheduleAsync(
            Editor, _store.Form.Id, new DateTime(2026, 12, 1, 8, 0, 0), new DateTime(2026, 12, 20, 18, 30, 0), 50, CancellationToken.None);

        Assert.Equal(LifecycleStatus.Ok, result.Status);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 7, 0, 0, TimeSpan.Zero), _store.Form.OpensAt);
        Assert.Equal(new DateTimeOffset(2026, 12, 20, 17, 30, 0, TimeSpan.Zero), _store.Form.ClosesAt);
        Assert.Equal(50, _store.Form.MaxResponses);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal("form.schedule", entry.Action);
        Assert.Equal("opens=2026-12-01T07:00Z;closes=2026-12-20T17:30Z;max=50", entry.Detail);
    }

    [Fact]
    public async Task Empty_values_remove_the_limits_and_an_unchanged_schedule_is_not_audited()
    {
        _store.Form = PublishedForm();
        _store.Form.SetSchedule(Now, Now.AddDays(1));
        _store.Form.SetMaxResponses(5);
        var service = Service();

        Assert.Equal(LifecycleStatus.Ok, (await service.UpdateScheduleAsync(Editor, _store.Form.Id, null, null, null, CancellationToken.None)).Status);
        Assert.Null(_store.Form.OpensAt);
        Assert.Null(_store.Form.ClosesAt);
        Assert.Null(_store.Form.MaxResponses);

        Assert.Equal(LifecycleStatus.Ok, (await service.UpdateScheduleAsync(Editor, _store.Form.Id, null, null, null, CancellationToken.None)).Status);
        Assert.Equal(1, _store.Saves);
        Assert.Single(_audit.Entries);
    }

    [Fact]
    public async Task Closing_date_before_opening_and_missing_spring_hour_are_refused()
    {
        _store.Form = PublishedForm();
        var service = Service();

        var reversed = await service.UpdateScheduleAsync(
            Editor, _store.Form.Id, new DateTime(2026, 12, 2, 8, 0, 0), new DateTime(2026, 12, 1, 8, 0, 0), null, CancellationToken.None);
        Assert.Equal(LifecycleStatus.Invalid, reversed.Status);
        Assert.Contains("postérieure", reversed.Error);

        var missingHour = await service.UpdateScheduleAsync(
            Editor, _store.Form.Id, new DateTime(2027, 3, 28, 2, 30, 0), null, null, CancellationToken.None);
        Assert.Equal(LifecycleStatus.Invalid, missingHour.Status);
        Assert.Contains("heure d'été", missingHour.Error);

        Assert.Null(_store.Form.OpensAt);
        Assert.Equal(0, _store.Saves);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Access_code_is_changed_hashed_and_never_logged()
    {
        _store.Form = PublishedForm("hash:1234");

        var result = await Service().ChangeAccessCodeAsync(Editor, _store.Form.Id, " 987654 ", "987654", CancellationToken.None);

        Assert.Equal(LifecycleStatus.Ok, result.Status);
        Assert.Equal("hash:987654", _store.Form.AccessCodeHash);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal("form.access_code", entry.Action);
        Assert.Null(entry.Detail);
    }

    [Theory]
    [InlineData("123", "123", "4 à 12 chiffres")]
    [InlineData("12ab56", "12ab56", "4 à 12 chiffres")]
    [InlineData("123456", "123465", "différentes")]
    public async Task Invalid_or_mistyped_access_code_is_refused(string code, string confirmation, string expected)
    {
        _store.Form = PublishedForm("hash:1234");

        var result = await Service().ChangeAccessCodeAsync(Editor, _store.Form.Id, code, confirmation, CancellationToken.None);

        Assert.Equal(LifecycleStatus.Invalid, result.Status);
        Assert.Contains(expected, result.Error);
        Assert.DoesNotContain(code, result.Error);
        Assert.Equal("hash:1234", _store.Form.AccessCodeHash);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Public_form_does_not_get_an_access_code_from_the_back_office()
    {
        _store.Form = PublishedForm();

        var result = await Service().ChangeAccessCodeAsync(Editor, _store.Form.Id, "123456", "123456", CancellationToken.None);

        Assert.Equal(LifecycleStatus.Invalid, result.Status);
        Assert.Null(_store.Form.AccessCodeHash);
    }

    [Fact]
    public void Availability_follows_status_dates_then_maximum()
    {
        var form = PublishedForm();
        Assert.Equal(FormAvailability.Open, FormLifecycleService.Availability(form, Now, 0));

        form.SetSchedule(Now.AddHours(1), Now.AddDays(1));
        Assert.Equal(FormAvailability.NotYetOpen, FormLifecycleService.Availability(form, Now, 0));
        Assert.Equal(FormAvailability.Ended, FormLifecycleService.Availability(form, Now.AddDays(1), 0));

        form.SetSchedule(null, null);
        form.SetMaxResponses(2);
        Assert.Equal(FormAvailability.Full, FormLifecycleService.Availability(form, Now, 2));
        Assert.Equal(FormAvailability.Open, FormLifecycleService.Availability(form, Now, 1));

        form.Close();
        Assert.Equal(FormAvailability.Closed, FormLifecycleService.Availability(form, Now, 0));

        var draft = new Form("brouillon", "Brouillon fictif", Now);
        Assert.Equal(FormAvailability.Draft, FormLifecycleService.Availability(draft, Now, 0));
    }

    [Fact]
    public void Availability_matches_what_the_public_form_accepts()
    {
        var form = PublishedForm();
        form.SetSchedule(Now.AddHours(-1), Now.AddHours(1));
        form.SetMaxResponses(3);

        foreach (var (at, count) in new[] { (Now, 0), (Now, 3), (Now.AddHours(-2), 0), (Now.AddHours(2), 0) })
        {
            Assert.Equal(
                form.IsAcceptingResponses(at, count),
                FormLifecycleService.Availability(form, at, count) == FormAvailability.Open);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore : IFormLifecycleStore
    {
        public Form Form { get; set; } = null!;

        public int ResponseCount { get; set; }

        public int Saves { get; private set; }

        public Task<Form?> FindAsync(Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult(Form is not null && Form.Id == formId ? Form : null);

        public Task<int> CountResponsesAsync(Guid formId, CancellationToken cancellationToken) => Task.FromResult(ResponseCount);

        public Task<DateTimeOffset?> GetDraftSavedAtAsync(Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult<DateTimeOffset?>(null);

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

    private sealed class FakeHasher : IAccessCodeHasher
    {
        public string Hash(string accessCode) => $"hash:{accessCode}";

        public bool Verify(string hash, string accessCode) => hash == $"hash:{accessCode}";
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

        public Task<string?> GetUserDisplayNameAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RequestPdfAsync(Guid submissionId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> ChangeStatusAsync(Guid submissionId, string status, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ExportData> GetExportDataAsync(SubmissionQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
