using System.Text.Json.Nodes;
using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.FormDefinitions;
using CseForms.Application.Pdf;
using CseForms.Application.Retention;
using Microsoft.Extensions.Logging.Abstractions;

namespace CseForms.Application.Tests;

public class SubmissionPresenterTests
{
    private static FormDefinition Definition()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "form-engine", "inscription-fictive.json");
        var fixture = JsonNode.Parse(File.ReadAllText(path))!;
        return FormDefinitionParser.Parse(fixture["definition"]!.ToJsonString()).Definition!;
    }

    [Fact]
    public void Values_are_presented_with_labels_and_french_formats()
    {
        var data = JsonNode.Parse("""
            {"civilite":"Mme","nom":"DUPONT","contrat":"CDD","dateEmbauche":"2026-09-01","dateFin":"2027-03-01",
             "adresse":{"street":"1 rue Fictive","postalCode":"63200","city":"RIOM"},
             "enfants":[{"nom":"DUPONT","prenom":"Léa","naissance":"2020-05-01"}],
             "attestation":true,"rgpdLu":false}
            """)!.AsObject();

        var sections = SubmissionPresenter.Present(Definition(), data);
        var values = sections.SelectMany(s => s.Items).OfType<PresentedValue>().ToDictionary(v => v.Label, v => v.Value);

        Assert.Equal("DUPONT", values["Nom"]);
        Assert.Equal("01/09/2026", values["Date d'embauche"]);
        Assert.Equal("01/03/2027", values["Date de fin"]);
        Assert.Equal("1 rue Fictive, 63200 RIOM", values["Adresse"]);
        Assert.Equal("Oui", values["J'atteste l'exactitude des informations"]);
        Assert.Equal("Non", values["J'ai lu la notice d'information"]);

        var children = Assert.Single(sections.SelectMany(s => s.Items).OfType<PresentedTable>());
        Assert.Equal(["Nom", "Prénom", "Date de naissance", "Sexe"], children.Headers);
        Assert.Equal(["DUPONT", "Léa", "01/05/2020", ""], Assert.Single(children.Rows));
    }

    [Fact]
    public void Absent_fields_and_empty_sections_are_not_presented()
    {
        var sections = SubmissionPresenter.Present(Definition(), JsonNode.Parse("""{"nom":"DUPONT"}""")!.AsObject());

        var section = Assert.Single(sections);
        Assert.Equal("État civil", section.Title);
        Assert.Single(section.Items);
    }

    [Theory]
    [InlineData("2026-03-29", "2026-03-28T23:00:00Z")] // passage à l'heure d'été ce jour-là
    [InlineData("2026-07-14", "2026-07-13T22:00:00Z")]
    [InlineData("2026-12-01", "2026-11-30T23:00:00Z")]
    public void Start_of_day_is_computed_in_paris(string day, string expectedUtc) =>
        Assert.Equal(
            DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture),
            CseCalendar.StartOfDayUtc(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture)));
}

public class BackOfficeServiceTests
{
    private static readonly Guid FormId = Guid.NewGuid();
    private static readonly Guid SubmissionId = Guid.NewGuid();

    private sealed class FakeStore(bool canAccess) : IBackOfficeStore
    {
        public SubmissionQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<FormSummary>> ListFormsAsync(CurrentUser user, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FormSummary>>([]);

        public Task<bool> CanAccessFormAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult(canAccess);

        public Task<FormHeader?> GetFormAsync(Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult<FormHeader?>(formId == FormId
                ? new FormHeader(FormId, "f", "F", """{"sections":[{"id":"s","fields":[{"id":"lieu","type":"site","label":"Site"}]}]}""")
                : null);

        public Task<SubmissionPage> ListSubmissionsAsync(SubmissionQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(new SubmissionPage([], 0, 1, 1, []));
        }

        public Task<SubmissionRecord?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) =>
            Task.FromResult<SubmissionRecord?>(submissionId == SubmissionId
                ? new SubmissionRecord(SubmissionId, FormId, "F", "TST-2026-0001", 1, """{"sections":[{"id":"s","fields":[{"id":"nom","type":"text","label":"Nom"}]}]}""", """{"nom":"DUPONT"}""", "new", DateTimeOffset.UnixEpoch, null, PdfWatermark: Watermark)
                : null);

        public bool Watermark { get; set; }

        public Task<string?> GetUserDisplayNameAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(userId == User.Id ? "Prénom Fictif" : null);

        public bool HasPdf { get; set; }

        public bool PdfPending { get; set; }

        public int PdfRequests { get; private set; }

        public Task<PdfState> GetPdfStateAsync(Guid submissionId, CancellationToken cancellationToken) =>
            Task.FromResult(new PdfState(HasPdf ? DateTimeOffset.UnixEpoch : null, PdfPending, Failed: false));

        public Task<StoredPdf?> GetPdfFileAsync(Guid submissionId, CancellationToken cancellationToken) =>
            Task.FromResult(HasPdf ? new StoredPdf(PdfFileId, "chemin") : null);

        public Task<bool> RequestPdfAsync(Guid submissionId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (PdfPending)
            {
                return Task.FromResult(false);
            }

            PdfRequests++;
            PdfPending = true;
            return Task.FromResult(true);
        }

        public string Status { get; private set; } = "new";

        public Task<string?> ChangeStatusAsync(Guid submissionId, string status, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (submissionId != SubmissionId || status == Status)
            {
                return Task.FromResult<string?>(null);
            }

            var previous = Status;
            Status = status;
            return Task.FromResult<string?>(previous);
        }

        public List<HistoryRecord> History { get; } = [];

        public Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(Guid submissionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HistoryRecord>>(History);

        public ExportData Export { get; set; } = new([], new Dictionary<int, string>());

        public Task<ExportData> GetExportDataAsync(SubmissionQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(Export);
        }
    }

    private sealed class FakeEraseStore : ISubmissionEraseStore
    {
        public List<Guid> Deleted { get; } = [];

        public List<DueSubmission> Due { get; } = [];

        /// <summary>Réponses dont le statut a changé entre la liste et la suppression : non supprimées.</summary>
        public HashSet<Guid> NoLongerDue { get; } = [];

        public Task<IReadOnlyList<DueSubmission>> ListDueAsync(DateTimeOffset now, int max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DueSubmission>>(Due.Where(d => !Deleted.Contains(d.Id) && !NoLongerDue.Contains(d.Id)).Take(max).ToList());

        public Task<IReadOnlyList<string>?> DeleteAsync(Guid submissionId, DateTimeOffset? dueAt, CancellationToken cancellationToken)
        {
            if ((dueAt is not null && NoLongerDue.Contains(submissionId)) || Deleted.Contains(submissionId))
            {
                return Task.FromResult<IReadOnlyList<string>?>(null);
            }

            Deleted.Add(submissionId);
            return Task.FromResult<IReadOnlyList<string>?>([$"files/{submissionId}.bin"]);
        }
    }

    private static readonly Guid PdfFileId = Guid.NewGuid();

    private static BackOfficeService Service(FakeStore store, FakeAudit audit, FakeEraseStore? erase = null, FakeVault? vault = null, FakeWatermarker? watermarker = null)
    {
        vault ??= new FakeVault();
        var eraser = new SubmissionEraser(erase ?? new FakeEraseStore(), vault, NullLogger<SubmissionEraser>.Instance);
        return new(store, audit, vault, eraser, watermarker ?? new FakeWatermarker(), new FixedClock(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero)));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeWatermarker : IPdfWatermarker
    {
        public List<PdfWatermarkText> Applied { get; } = [];

        public byte[] Apply(byte[] pdf, PdfWatermarkText text)
        {
            Applied.Add(text);
            return [.. pdf, .. "+filigrane"u8.ToArray()];
        }
    }

    private sealed class FakeVault : IFileVault
    {
        public List<string> DeletedPaths { get; } = [];

        public Task<StoredFile> WriteAsync(Guid fileId, Guid submissionId, byte[] content, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<byte[]> ReadAsync(string storagePath, Guid fileId, CancellationToken cancellationToken) =>
            Task.FromResult(fileId == PdfFileId ? "%PDF-fictif"u8.ToArray() : throw new InvalidOperationException());

        public void Delete(string storagePath) => DeletedPaths.Add(storagePath);
    }

    private sealed class FakeAudit : IAuditLog
    {
        public List<(string Action, Guid? UserId, string? Target)> Entries { get; } = [];

        public List<string?> Details { get; } = [];

        public Task RecordAsync(string action, Guid? userId, string? target, string? detail, CancellationToken cancellationToken)
        {
            Entries.Add((action, userId, target));
            Details.Add(detail);
            return Task.CompletedTask;
        }
    }

    private static readonly CurrentUser User = new(Guid.NewGuid(), IsAdmin: false);

    [Fact]
    public async Task Viewing_a_submission_is_audited()
    {
        var audit = new FakeAudit();
        var detail = await Service(new FakeStore(canAccess: true), audit).GetSubmissionAsync(User, SubmissionId, default);

        Assert.NotNull(detail);
        Assert.Equal("DUPONT", Assert.IsType<PresentedValue>(detail.Sections[0].Items[0]).Value);
        Assert.Equal((AuditActions.SubmissionViewed, (Guid?)User.Id, $"submission:{SubmissionId}"), Assert.Single(audit.Entries));
    }

    [Fact]
    public async Task Denied_access_looks_like_not_found_and_is_audited()
    {
        var audit = new FakeAudit();
        var service = Service(new FakeStore(canAccess: false), audit);

        Assert.Null(await service.GetSubmissionAsync(User, SubmissionId, default));
        Assert.Null(await service.ListSubmissionsAsync(User, FormId, new SubmissionFilter(), default));
        Assert.All(audit.Entries, e => Assert.Equal(AuditActions.AccessDenied, e.Action));
        Assert.Equal(2, audit.Entries.Count);
    }

    [Fact]
    public async Task Unknown_submission_is_not_audited()
    {
        var audit = new FakeAudit();

        Assert.Null(await Service(new FakeStore(canAccess: true), audit).GetSubmissionAsync(User, Guid.NewGuid(), default));
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Filter_is_normalized_and_period_converted_to_utc()
    {
        var store = new FakeStore(canAccess: true);
        var filter = new SubmissionFilter(" tst-2026 ", " new ", " Riom ", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), Page: 0);

        await Service(store, new FakeAudit()).ListSubmissionsAsync(User, FormId, filter, default);

        var query = store.LastQuery!;
        Assert.Equal("TST-2026", query.CaseNumberContains);
        Assert.Equal("new", query.Status);
        Assert.Equal("lieu", query.SiteFieldId);
        Assert.Equal("Riom", query.Site);
        Assert.Equal(new DateTimeOffset(2026, 6, 30, 22, 0, 0, TimeSpan.Zero), query.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 22, 0, 0, TimeSpan.Zero), query.ToUtcExclusive);
        Assert.Equal(1, query.Page);
    }

    [Fact]
    public async Task Pdf_download_is_audited_and_named_after_the_case_number()
    {
        var audit = new FakeAudit();
        var download = await Service(new FakeStore(canAccess: true) { HasPdf = true }, audit).DownloadPdfAsync(User, SubmissionId, default);

        Assert.NotNull(download);
        Assert.Equal("TST-2026-0001.pdf", download.FileName);
        Assert.Equal((AuditActions.SubmissionDownloaded, (Guid?)User.Id, $"submission:{SubmissionId}"), Assert.Single(audit.Entries));
    }

    [Fact]
    public async Task Pdf_is_left_as_stored_when_the_form_has_no_watermark()
    {
        var watermarker = new FakeWatermarker();
        var download = await Service(new FakeStore(canAccess: true) { HasPdf = true }, new FakeAudit(), watermarker: watermarker).DownloadPdfAsync(User, SubmissionId, default);

        Assert.Equal("%PDF-fictif"u8.ToArray(), download!.Content);
        Assert.Empty(watermarker.Applied);
    }

    [Fact]
    public async Task Watermark_names_the_downloader_and_the_paris_date_and_is_audited()
    {
        var audit = new FakeAudit();
        var watermarker = new FakeWatermarker();
        var store = new FakeStore(canAccess: true) { HasPdf = true, Watermark = true };

        var download = await Service(store, audit, watermarker: watermarker).DownloadPdfAsync(User, SubmissionId, default);

        Assert.EndsWith("+filigrane", System.Text.Encoding.UTF8.GetString(download!.Content));
        var text = Assert.Single(watermarker.Applied);
        Assert.Equal("Confidentiel CSE", text.Title);
        Assert.Equal("Téléchargé par Prénom Fictif le 01/01/2027", text.Detail); // 23h30 UTC le 31/12 : déjà le 1er janvier à Paris
        Assert.Equal(AuditActions.SubmissionDownloaded, Assert.Single(audit.Entries).Action);
        Assert.Equal("watermark", Assert.Single(audit.Details));
    }

    [Fact]
    public async Task Pdf_download_without_right_or_file_returns_nothing()
    {
        var audit = new FakeAudit();

        Assert.Null(await Service(new FakeStore(canAccess: false) { HasPdf = true }, audit).DownloadPdfAsync(User, SubmissionId, default));
        Assert.Equal(AuditActions.AccessDenied, Assert.Single(audit.Entries).Action);

        Assert.Null(await Service(new FakeStore(canAccess: true), new FakeAudit()).DownloadPdfAsync(User, SubmissionId, default));
    }

    [Fact]
    public async Task Regeneration_is_refused_to_readers_and_not_duplicated()
    {
        var store = new FakeStore(canAccess: true);
        var audit = new FakeAudit();
        var service = Service(store, audit);

        Assert.Equal(PdfRequestOutcome.Forbidden, await service.RegeneratePdfAsync(User with { IsReadOnly = true }, SubmissionId, default));
        Assert.Equal(PdfRequestOutcome.Requested, await service.RegeneratePdfAsync(User, SubmissionId, default));
        Assert.Equal(PdfRequestOutcome.AlreadyPending, await service.RegeneratePdfAsync(User, SubmissionId, default));
        Assert.Equal(PdfRequestOutcome.NotFound, await service.RegeneratePdfAsync(User, Guid.NewGuid(), default));

        Assert.Equal(1, store.PdfRequests);
        Assert.Equal([AuditActions.AccessDenied, AuditActions.PdfRegenerationRequested], audit.Entries.Select(e => e.Action));
    }

    private static readonly CurrentUser Admin = new(Guid.NewGuid(), IsAdmin: true);

    [Fact]
    public async Task Status_change_is_audited_with_previous_and_new_status()
    {
        var store = new FakeStore(canAccess: true);
        var audit = new FakeAudit();
        var service = Service(store, audit);

        Assert.Equal(StatusChangeOutcome.Changed, await service.ChangeStatusAsync(User, SubmissionId, " entered ", default));
        Assert.Equal(StatusChangeOutcome.Unchanged, await service.ChangeStatusAsync(User, SubmissionId, "entered", default));

        Assert.Equal("entered", store.Status);
        Assert.Equal((AuditActions.SubmissionStatusChanged, (Guid?)User.Id, $"submission:{SubmissionId}"), Assert.Single(audit.Entries));
        Assert.Equal("new>entered", Assert.Single(audit.Details));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("supprimee")]
    public async Task Unknown_status_is_refused(string? status)
    {
        var store = new FakeStore(canAccess: true);
        var audit = new FakeAudit();

        Assert.Equal(StatusChangeOutcome.Invalid, await Service(store, audit).ChangeStatusAsync(User, SubmissionId, status, default));
        Assert.Equal("new", store.Status);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Readers_and_users_without_right_cannot_change_status()
    {
        var audit = new FakeAudit();
        var store = new FakeStore(canAccess: true);

        Assert.Equal(StatusChangeOutcome.Forbidden, await Service(store, audit).ChangeStatusAsync(User with { IsReadOnly = true }, SubmissionId, "entered", default));
        Assert.Equal(StatusChangeOutcome.NotFound, await Service(new FakeStore(canAccess: false), audit).ChangeStatusAsync(User, SubmissionId, "entered", default));

        Assert.Equal("new", store.Status);
        Assert.All(audit.Entries, e => Assert.Equal(AuditActions.AccessDenied, e.Action));
    }

    [Fact]
    public async Task Deletion_is_reserved_to_admins()
    {
        var erase = new FakeEraseStore();
        var audit = new FakeAudit();

        var result = await Service(new FakeStore(canAccess: true), audit, erase).DeleteSubmissionAsync(User, SubmissionId, "TST-2026-0001", default);

        Assert.Equal(DeletionOutcome.Forbidden, result.Outcome);
        Assert.Empty(erase.Deleted);
        Assert.Equal(AuditActions.AccessDenied, Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task Deletion_screen_is_reserved_to_admins_and_refusal_is_audited()
    {
        var audit = new FakeAudit();

        var refused = await Service(new FakeStore(canAccess: true), audit).GetDeletionScreenAsync(User, SubmissionId, default);
        var hidden = await Service(new FakeStore(canAccess: false), audit).GetDeletionScreenAsync(Admin, SubmissionId, default);
        var shown = await Service(new FakeStore(canAccess: true), audit).GetDeletionScreenAsync(Admin, SubmissionId, default);

        Assert.Equal(DeletionOutcome.Forbidden, refused.Outcome);
        Assert.Null(refused.Screen);
        Assert.Equal(DeletionOutcome.NotFound, hidden.Outcome);
        Assert.Null(hidden.Screen);
        Assert.Equal(SubmissionId, shown.Screen!.Id);

        // Un refus par rôle insuffisant, un par formulaire non attribué ; rien pour l'écran affiché.
        Assert.Equal(
            [(AuditActions.AccessDenied, User.Id), (AuditActions.AccessDenied, Admin.Id)],
            audit.Entries.Select(e => (e.Action, e.UserId!.Value)));
        Assert.All(audit.Entries, e => Assert.Equal($"submission:{SubmissionId}", e.Target));
    }

    [Fact]
    public async Task Deletion_requires_the_case_number_as_confirmation()
    {
        var erase = new FakeEraseStore();
        var audit = new FakeAudit();

        var result = await Service(new FakeStore(canAccess: true), audit, erase).DeleteSubmissionAsync(Admin, SubmissionId, "TST-2026-0002", default);

        Assert.Equal(DeletionOutcome.NotConfirmed, result.Outcome);
        Assert.Empty(erase.Deleted);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Deletion_erases_rows_and_files_and_keeps_the_case_number_in_audit()
    {
        var erase = new FakeEraseStore();
        var vault = new FakeVault();
        var audit = new FakeAudit();

        var result = await Service(new FakeStore(canAccess: true), audit, erase, vault).DeleteSubmissionAsync(Admin, SubmissionId, " tst-2026-0001 ", default);

        Assert.Equal(new DeletionResult(DeletionOutcome.Deleted, FormId), result);
        Assert.Equal([SubmissionId], erase.Deleted);
        Assert.Equal([$"files/{SubmissionId}.bin"], vault.DeletedPaths);
        Assert.Equal((AuditActions.SubmissionDeleted, (Guid?)Admin.Id, $"submission:{SubmissionId}"), Assert.Single(audit.Entries));
        Assert.Equal("TST-2026-0001", Assert.Single(audit.Details));
    }

    [Fact]
    public async Task Export_is_refused_to_readers()
    {
        var audit = new FakeAudit();

        var result = await Service(new FakeStore(canAccess: true), audit).ExportCsvAsync(User with { IsReadOnly = true }, FormId, new SubmissionFilter(), default);

        Assert.Equal(ExportOutcome.Forbidden, result.Outcome);
        Assert.Null(result.File);
        Assert.Equal(AuditActions.AccessDenied, Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task Export_uses_the_list_filters_and_is_audited_with_its_row_count()
    {
        var store = new FakeStore(canAccess: true)
        {
            Export = new ExportData(
                [new ExportRow("TST-2026-0001", DateTimeOffset.UnixEpoch, "new", 1, """{"lieu":"Riom"}""")],
                new Dictionary<int, string> { [1] = """{"sections":[{"id":"s","fields":[{"id":"lieu","type":"site","label":"Site"}]}]}""" }),
        };
        var audit = new FakeAudit();

        var result = await Service(store, audit).ExportCsvAsync(User, FormId, new SubmissionFilter(Site: " Riom ", Page: 4), default);

        Assert.Equal(ExportOutcome.Ok, result.Outcome);
        Assert.StartsWith("f-reponses-", result.File!.FileName, StringComparison.Ordinal);
        Assert.EndsWith(".csv", result.File.FileName, StringComparison.Ordinal);
        Assert.Equal(1, result.File.RowCount);
        Assert.Equal("Riom", store.LastQuery!.Site);
        Assert.Equal((AuditActions.SubmissionsExported, (Guid?)User.Id, $"form:{FormId}"), Assert.Single(audit.Entries));
        Assert.Equal("1", Assert.Single(audit.Details));
    }

    [Fact]
    public async Task Detail_shows_history_with_reception_first_in_time()
    {
        var store = new FakeStore(canAccess: true);
        store.History.Add(new HistoryRecord(DateTimeOffset.UnixEpoch.AddDays(2), AuditActions.SubmissionStatusChanged, "new>entered", "Élu fictif"));
        store.History.Add(new HistoryRecord(DateTimeOffset.UnixEpoch.AddDays(1), "job.pdf.succeeded", null, null));

        var detail = await Service(store, new FakeAudit()).GetSubmissionAsync(User, SubmissionId, default);

        Assert.Equal(
            ["Statut changé : « Nouvelle » vers « Saisie dans le logiciel CSE »", "PDF généré", "Réponse reçue"],
            detail!.History.Select(h => h.Label));
        Assert.Equal("Élu fictif", detail.History[0].Actor);
    }
}
