using CseForms.Application.BackOffice;
using CseForms.Domain.Audit;
using CseForms.Domain.Forms;
using CseForms.Domain.Jobs;
using CseForms.Domain.Submissions;
using CseForms.Infrastructure.BackOffice;
using CseForms.Infrastructure.Identity;
using CseForms.Infrastructure.Retention;
using CseForms.Testing;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Tests;

/// <summary>Statuts, historique, export et suppression des réponses, sur une vraie base PostgreSQL.</summary>
public class ProcessingStoreTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Day = new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);

    private async Task<(Form Form, Guid SubmissionId)> SeedAsync(string slug, int? retentionDays = null)
    {
        await using var db = fixture.CreateContext();
        var form = await TestData.AddPublishedFormAsync(db, slug, configure: f => f.SetRetentionDays(retentionDays));
        var submission = form.CreateSubmission($"{slug}-0001", """{"nom":"FICTIF","site":"Riom"}""", Day, 0);
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();
        return (form, submission.Id);
    }

    [Fact]
    public async Task Status_change_starts_retention_from_processing()
    {
        var (_, id) = await SeedAsync("tr-statut", retentionDays: 180);
        var processedAt = Day.AddDays(3);

        await using (var db = fixture.CreateContext())
        {
            var store = new BackOfficeStore(db);
            Assert.Equal("new", await store.ChangeStatusAsync(id, "entered", processedAt, default));
            Assert.Null(await store.ChangeStatusAsync(id, "entered", processedAt, default));
            Assert.Null(await store.ChangeStatusAsync(Guid.NewGuid(), "entered", processedAt, default));
        }

        await using (var db = fixture.CreateContext())
        {
            var submission = await db.Submissions.SingleAsync(s => s.Id == id);
            Assert.Equal("entered", submission.Status);
            Assert.Equal(processedAt.AddDays(180), submission.PurgeAt);
        }
    }

    [Fact]
    public async Task History_lists_actions_with_author_and_finished_jobs_but_not_views()
    {
        var (_, id) = await SeedAsync("tr-historique");
        var target = $"submission:{id}";

        await using (var db = fixture.CreateContext())
        {
            var user = new AppUser { Id = Guid.NewGuid(), UserName = "tr-historique@example.test", Email = "tr-historique@example.test", DisplayName = "Élu fictif" };
            db.Users.Add(user);
            db.AuditLog.Add(new AuditEntry(user.Id, AuditActions.SubmissionViewed, target, Day.AddHours(1)));
            db.AuditLog.Add(new AuditEntry(user.Id, AuditActions.SubmissionStatusChanged, target, Day.AddHours(2), "new>entered"));
            db.AuditLog.Add(new AuditEntry(user.Id, AuditActions.SubmissionStatusChanged, "submission:autre", Day.AddHours(2), "new>entered"));

            var done = new SubmissionJob(id, JobType.Pdf, Day);
            done.MarkSucceeded(Day.AddMinutes(1));
            db.Jobs.Add(done);
            db.Jobs.Add(new SubmissionJob(id, JobType.Notification, Day));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var history = await new BackOfficeStore(db).GetHistoryAsync(id, default);

            Assert.Equal(2, history.Count);
            Assert.Equal(new HistoryRecord(Day.AddHours(2), AuditActions.SubmissionStatusChanged, "new>entered", "Élu fictif"), history[0]);
            Assert.Equal(new HistoryRecord(Day.AddMinutes(1), "job.pdf.succeeded", null, null), history[1]);
        }
    }

    [Fact]
    public async Task Export_data_applies_filters_without_paging_and_brings_the_filled_versions()
    {
        await using var db = fixture.CreateContext();
        var form = await TestData.AddPublishedFormAsync(db, "tr-export");
        for (var i = 1; i <= SubmissionFilter.PageSize + 5; i++)
        {
            db.Submissions.Add(form.CreateSubmission($"tr-export-{i:D4}", $$"""{"nom":"FICTIF","site":"{{(i % 2 == 0 ? "Riom" : "Abrest")}}"}""", Day.AddMinutes(i), i - 1));
        }

        await db.SaveChangesAsync();
        form.Publish(TestData.SimpleDefinition.Replace("\"Nom\"", "\"Nom de famille\"", StringComparison.Ordinal), null, null, Day, "test");
        await db.SaveChangesAsync();
        db.Submissions.Add(form.CreateSubmission("tr-export-9999", """{"nom":"FICTIF","site":"Riom"}""", Day.AddDays(1), 100));
        await db.SaveChangesAsync();

        var store = new BackOfficeStore(db);
        var all = await store.GetExportDataAsync(new SubmissionQuery(form.Id, null, null, "site", null, null, null, false, 1), default);
        var riom = await store.GetExportDataAsync(new SubmissionQuery(form.Id, null, null, "site", "Riom", null, null, true, 1), default);

        Assert.Equal(SubmissionFilter.PageSize + 6, all.Rows.Count);
        Assert.Equal("tr-export-9999", all.Rows[0].CaseNumber);
        Assert.Equal(2, all.Rows[0].VersionNumber);
        Assert.Equal([1, 2], all.SchemasByVersion.Keys.Order());
        Assert.Contains("Nom de famille", all.SchemasByVersion[2], StringComparison.Ordinal);

        Assert.Equal(16, riom.Rows.Count);
        Assert.Equal("tr-export-0002", riom.Rows[0].CaseNumber);
    }

    [Fact]
    public async Task Delete_removes_the_submission_with_its_files_and_jobs()
    {
        var (_, id) = await SeedAsync("tr-suppression");
        await using (var db = fixture.CreateContext())
        {
            db.SubmissionFiles.Add(new SubmissionFile(Guid.NewGuid(), id, SubmissionFileKind.Pdf, "ab/cd.bin", new string('A', 64), 1, Day));
            db.Jobs.Add(new SubmissionJob(id, JobType.Pdf, Day));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var store = new SubmissionEraseStore(db);
            Assert.Equal(["ab/cd.bin"], await store.DeleteAsync(id, dueAt: null, default));
            Assert.Null(await store.DeleteAsync(id, dueAt: null, default));
        }

        await using (var db = fixture.CreateContext())
        {
            Assert.False(await db.Submissions.AnyAsync(s => s.Id == id));
            Assert.False(await db.SubmissionFiles.AnyAsync(f => f.SubmissionId == id));
            Assert.False(await db.Jobs.AnyAsync(j => j.SubmissionId == id));
        }
    }

    [Fact]
    public async Task Purge_only_takes_submissions_whose_date_is_reached()
    {
        var (_, due) = await SeedAsync("tr-purge-due", retentionDays: 30);
        var (_, later) = await SeedAsync("tr-purge-tard", retentionDays: 30);
        var (_, untreated) = await SeedAsync("tr-purge-neuve", retentionDays: 30);
        var now = Day.AddDays(40);

        await using (var db = fixture.CreateContext())
        {
            var store = new BackOfficeStore(db);
            await store.ChangeStatusAsync(due, "entered", Day, default);
            await store.ChangeStatusAsync(later, "entered", Day.AddDays(20), default);
        }

        await using (var db = fixture.CreateContext())
        {
            var store = new SubmissionEraseStore(db);
            var listed = await store.ListDueAsync(now, 1000, default);

            Assert.Contains(listed, d => d.Id == due && d.CaseNumber == "tr-purge-due-0001");
            Assert.DoesNotContain(listed, d => d.Id == later || d.Id == untreated);

            // Garde-fou à la suppression : une réponse non échue n'est jamais supprimée par la purge.
            Assert.Null(await store.DeleteAsync(later, now, default));
            Assert.Null(await store.DeleteAsync(untreated, now, default));
            Assert.NotNull(await store.DeleteAsync(due, now, default));
        }

        await using (var db = fixture.CreateContext())
        {
            Assert.False(await db.Submissions.AnyAsync(s => s.Id == due));
            Assert.Equal(2, await db.Submissions.CountAsync(s => s.Id == later || s.Id == untreated));
        }
    }

    [Fact]
    public async Task Audit_detail_is_persisted()
    {
        await using (var db = fixture.CreateContext())
        {
            await new AuditLog(db, TimeProvider.System).RecordAsync(AuditActions.SubmissionDeleted, null, "submission:detail", "TST-2026-0042", default);
        }

        await using (var db = fixture.CreateContext())
        {
            Assert.Equal("TST-2026-0042", (await db.AuditLog.SingleAsync(a => a.Target == "submission:detail")).Detail);
        }
    }
}
