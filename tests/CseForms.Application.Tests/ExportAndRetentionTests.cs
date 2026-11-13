using System.Text;
using CseForms.Application.BackOffice;
using CseForms.Application.Pdf;
using CseForms.Application.Retention;
using Microsoft.Extensions.Logging.Abstractions;

namespace CseForms.Application.Tests;

public class SubmissionCsvExporterTests
{
    private const string Version1 = """
        {"sections":[{"id":"s","fields":[
          {"id":"intro","type":"info","text":"Bienvenue"},
          {"id":"nom","type":"text","label":"Nom"},
          {"id":"ancien","type":"text","label":"Champ retiré"},
          {"id":"email","type":"email","label":"Email"},
          {"id":"emailConfirm","type":"email","label":"Confirmation","persist":false}
        ]}]}
        """;

    private const string Version2 = """
        {"sections":[{"id":"s","fields":[
          {"id":"nom","type":"text","label":"Nom de famille"},
          {"id":"naissance","type":"date","label":"Date de naissance"},
          {"id":"adresse","type":"address","label":"Adresse"},
          {"id":"attestation","type":"consent","label":"Attestation"},
          {"id":"enfants","type":"repeat","label":"Enfants","fields":[
            {"id":"prenom","type":"text","label":"Prénom"},
            {"id":"ne","type":"date","label":"Né le"}
          ]}
        ]}]}
        """;

    private static string[] Lines(byte[] content)
    {
        Assert.Equal(Encoding.UTF8.GetPreamble(), content[..3]);
        return Encoding.UTF8.GetString(content[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    private static ExportData Data(params ExportRow[] rows) =>
        new(rows, new Dictionary<int, string> { [1] = Version1, [2] = Version2 });

    [Fact]
    public void Columns_follow_the_latest_version_then_fields_of_older_versions()
    {
        var lines = Lines(SubmissionCsvExporter.Write(Data(
            new ExportRow("TST-2026-0002", new DateTimeOffset(2026, 7, 1, 8, 30, 0, TimeSpan.Zero), "entered", 2,
                """{"nom":"DUPONT","naissance":"1990-02-03","adresse":{"street":"1 rue Fictive","postalCode":"63200","city":"RIOM"},"attestation":true,"enfants":[{"prenom":"Léa","ne":"2020-05-01"},{"prenom":"Tom"}]}"""),
            new ExportRow("TST-2026-0001", new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), "new", 1,
                """{"nom":"MARTIN","ancien":"valeur","email":"martin@example.test"}"""))));

        Assert.Equal(
            "Dossier;Reçue le;Statut;Version;Nom de famille;Date de naissance;Adresse (voie);Adresse (code postal);Adresse (ville);Attestation;"
            + "Enfants 1 - Prénom;Enfants 1 - Né le;Enfants 2 - Prénom;Enfants 2 - Né le;Champ retiré;Email",
            lines[0]);
        Assert.Equal(
            "TST-2026-0002;01/07/2026 10:30;Saisie dans le logiciel CSE;2;DUPONT;03/02/1990;1 rue Fictive;63200;RIOM;Oui;Léa;01/05/2020;Tom;;;",
            lines[1]);
        Assert.Equal("TST-2026-0001;15/01/2026 13:00;Nouvelle;1;MARTIN;;;;;;;;;;valeur;martin@example.test", lines[2]);
    }

    [Fact]
    public void Empty_export_still_has_headers()
    {
        var lines = Lines(SubmissionCsvExporter.Write(new ExportData([], new Dictionary<int, string>())));

        Assert.Equal("Dossier;Reçue le;Statut;Version", Assert.Single(lines));
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+33612345678", "'+33612345678")]
    [InlineData("-2", "'-2")]
    [InlineData("@SOMME(A1)", "'@SOMME(A1)")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("dit \"bonjour\"", "\"dit \"\"bonjour\"\"\"")]
    [InlineData("ligne\nsuivante", "\"ligne\nsuivante\"")]
    [InlineData("DUPONT", "DUPONT")]
    public void Cells_are_escaped_and_formulas_neutralized(string value, string expected) =>
        Assert.Equal(expected, SubmissionCsvExporter.Escape(value));

    [Fact]
    public void Unexpected_value_type_is_exported_as_is()
    {
        var lines = Lines(SubmissionCsvExporter.Write(Data(
            new ExportRow("TST-2026-0003", DateTimeOffset.UnixEpoch, "new", 2, """{"attestation":"oui"}"""))));

        Assert.Contains(";oui;", lines[1], StringComparison.Ordinal);
    }
}

public class SubmissionHistoryTests
{
    [Fact]
    public void Unknown_actions_are_shown_by_their_code()
    {
        Assert.Equal("autre.action", SubmissionHistory.Describe("autre.action", null));
        Assert.Equal("Statut changé", SubmissionHistory.Describe(AuditActions.SubmissionStatusChanged, null));
        Assert.Equal("Accusé de réception envoyé", SubmissionHistory.Describe("job.receipt.succeeded", null));
    }
}

public class RetentionServiceTests
{
    private sealed class FakeEraseStore : ISubmissionEraseStore
    {
        public List<DueSubmission> Due { get; } = [];

        public HashSet<Guid> Deleted { get; } = [];

        public HashSet<Guid> NoLongerDue { get; } = [];

        public List<DateTimeOffset?> DueChecks { get; } = [];

        public Task<IReadOnlyList<DueSubmission>> ListDueAsync(DateTimeOffset now, int max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DueSubmission>>(Due.Where(d => !Deleted.Contains(d.Id)).Take(max).ToList());

        public Task<IReadOnlyList<string>?> DeleteAsync(Guid submissionId, DateTimeOffset? dueAt, CancellationToken cancellationToken)
        {
            DueChecks.Add(dueAt);
            if (NoLongerDue.Contains(submissionId))
            {
                return Task.FromResult<IReadOnlyList<string>?>(null);
            }

            Deleted.Add(submissionId);
            return Task.FromResult<IReadOnlyList<string>?>([$"f/{submissionId}"]);
        }
    }

    private sealed class FakeVault : IFileVault
    {
        public List<string> Deleted { get; } = [];

        public bool Fail { get; set; }

        public Task<StoredFile> WriteAsync(Guid fileId, Guid submissionId, byte[] content, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<byte[]> ReadAsync(string storagePath, Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Delete(string storagePath)
        {
            if (Fail)
            {
                throw new IOException("disque indisponible");
            }

            Deleted.Add(storagePath);
        }
    }

    private sealed class FakeAudit : IAuditLog
    {
        public List<(string Action, Guid? UserId, string? Target, string? Detail)> Entries { get; } = [];

        public Task RecordAsync(string action, Guid? userId, string? target, string? detail, CancellationToken cancellationToken)
        {
            Entries.Add((action, userId, target, detail));
            return Task.CompletedTask;
        }
    }

    private static RetentionService Service(FakeEraseStore store, FakeVault vault, FakeAudit audit) =>
        new(store, new SubmissionEraser(store, vault, NullLogger<SubmissionEraser>.Instance), audit, TimeProvider.System, NullLogger<RetentionService>.Instance);

    [Fact]
    public async Task Due_submissions_are_erased_in_batches_and_audited_without_user()
    {
        var store = new FakeEraseStore();
        for (var i = 1; i <= RetentionService.BatchSize + 5; i++)
        {
            store.Due.Add(new DueSubmission(Guid.NewGuid(), $"TST-2026-{i:D4}"));
        }

        var vault = new FakeVault();
        var audit = new FakeAudit();

        Assert.Equal(RetentionService.BatchSize + 5, await Service(store, vault, audit).PurgeDueAsync(default));

        Assert.Equal(RetentionService.BatchSize + 5, vault.Deleted.Count);
        Assert.All(store.DueChecks, d => Assert.NotNull(d));
        Assert.All(audit.Entries, e =>
        {
            Assert.Equal(AuditActions.SubmissionPurged, e.Action);
            Assert.Null(e.UserId);
        });
        Assert.Equal("TST-2026-0001", audit.Entries[0].Detail);
    }

    [Fact]
    public async Task Submission_back_to_new_meanwhile_is_kept()
    {
        var store = new FakeEraseStore();
        var kept = new DueSubmission(Guid.NewGuid(), "TST-2026-0001");
        store.Due.Add(kept);
        store.NoLongerDue.Add(kept.Id);
        var audit = new FakeAudit();

        Assert.Equal(0, await Service(store, new FakeVault(), audit).PurgeDueAsync(default));
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task File_that_cannot_be_deleted_does_not_stop_the_purge()
    {
        var store = new FakeEraseStore();
        store.Due.Add(new DueSubmission(Guid.NewGuid(), "TST-2026-0001"));
        store.Due.Add(new DueSubmission(Guid.NewGuid(), "TST-2026-0002"));

        Assert.Equal(2, await Service(store, new FakeVault { Fail = true }, new FakeAudit()).PurgeDueAsync(default));
    }
}
