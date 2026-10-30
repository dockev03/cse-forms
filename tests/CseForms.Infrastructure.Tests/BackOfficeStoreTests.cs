using CseForms.Application.BackOffice;
using CseForms.Domain.Forms;
using CseForms.Infrastructure.BackOffice;
using CseForms.Infrastructure.Identity;
using CseForms.Testing;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Tests;

public class BackOfficeStoreTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Day = new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);

    private async Task<(Form Form, Guid UserId)> SeedAsync(string slug, int count, bool grant)
    {
        await using var db = fixture.CreateContext();
        var form = await TestData.AddPublishedFormAsync(db, slug);
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"{slug}@example.test", Email = $"{slug}@example.test", DisplayName = "Élu fictif" };
        db.Users.Add(user);
        if (grant)
        {
            db.UserFormAccess.Add(new UserFormAccess { UserId = user.Id, FormId = form.Id });
        }

        for (var i = 1; i <= count; i++)
        {
            var submission = form.CreateSubmission(
                $"{slug}-{i:D4}",
                $$"""{"nom":"FICTIF","site":"{{(i % 2 == 0 ? "Riom" : "Abrest")}}"}""",
                Day.AddDays(i),
                i - 1);
            if (i % 3 == 0)
            {
                submission.ChangeStatus("entered");
            }

            db.Submissions.Add(submission);
        }

        await db.SaveChangesAsync();
        return (form, user.Id);
    }

    private static SubmissionQuery Query(Guid formId, int page = 1) =>
        new(formId, null, null, "site", null, null, null, false, page);

    [Fact]
    public async Task Non_admin_sees_only_granted_forms_and_admin_sees_all()
    {
        var (granted, userId) = await SeedAsync("bo-attribue", 2, grant: true);
        var (other, _) = await SeedAsync("bo-autre", 1, grant: false);

        await using var db = fixture.CreateContext();
        var store = new BackOfficeStore(db);

        var userForms = await store.ListFormsAsync(new CurrentUser(userId, false), default);
        var adminForms = await store.ListFormsAsync(new CurrentUser(Guid.NewGuid(), true), default);

        Assert.Equal(granted.Id, Assert.Single(userForms).Id);
        Assert.Equal(2, userForms[0].ResponseCount);
        Assert.Contains(adminForms, f => f.Id == other.Id);
        Assert.True(await store.CanAccessFormAsync(new CurrentUser(userId, false), granted.Id, default));
        Assert.False(await store.CanAccessFormAsync(new CurrentUser(userId, false), other.Id, default));
    }

    [Fact]
    public async Task Submissions_are_paged_newest_first()
    {
        var (form, _) = await SeedAsync("bo-pages", 30, grant: false);
        await using var db = fixture.CreateContext();
        var store = new BackOfficeStore(db);

        var first = await store.ListSubmissionsAsync(Query(form.Id), default);
        var last = await store.ListSubmissionsAsync(Query(form.Id, page: 99), default);

        Assert.Equal(30, first.TotalCount);
        Assert.Equal(2, first.PageCount);
        Assert.Equal(SubmissionFilter.PageSize, first.Items.Count);
        Assert.True(first.Items[0].CreatedAt > first.Items[1].CreatedAt);
        Assert.Equal(2, last.Page);
        Assert.Equal(5, last.Items.Count);
        Assert.Equal(["entered", "new"], first.Statuses);
    }

    [Fact]
    public async Task Filters_combine_status_site_period_and_case_number()
    {
        var (form, _) = await SeedAsync("bo-filtres", 12, grant: false);
        await using var db = fixture.CreateContext();
        var store = new BackOfficeStore(db);

        var bySite = await store.ListSubmissionsAsync(Query(form.Id) with { Site = "Riom" }, default);
        var byStatus = await store.ListSubmissionsAsync(Query(form.Id) with { Status = "entered" }, default);
        var byPeriod = await store.ListSubmissionsAsync(
            Query(form.Id) with { FromUtc = Day.AddDays(3), ToUtcExclusive = Day.AddDays(6) }, default);
        var byNumber = await store.ListSubmissionsAsync(Query(form.Id) with { CaseNumberContains = "-0007" }, default);
        var injection = await store.ListSubmissionsAsync(Query(form.Id) with { Site = "Riom\"}' OR 1=1 --" }, default);

        Assert.Equal(6, bySite.TotalCount);
        Assert.All(bySite.Items, i => Assert.Equal("Riom", i.Site));
        Assert.Equal(4, byStatus.TotalCount);
        Assert.Equal(3, byPeriod.TotalCount);
        Assert.EndsWith("-0007", Assert.Single(byNumber.Items).CaseNumber);
        Assert.Equal(0, injection.TotalCount);
    }

    [Fact]
    public async Task Submission_record_carries_its_version_and_form()
    {
        var (form, _) = await SeedAsync("bo-fiche", 1, grant: false);
        await using var db = fixture.CreateContext();
        var id = await db.Submissions.Where(s => s.FormVersionId == form.CurrentVersionId).Select(s => s.Id).SingleAsync();

        var record = await new BackOfficeStore(db).GetSubmissionAsync(id, default);

        Assert.NotNull(record);
        Assert.Equal(form.Id, record.FormId);
        Assert.Equal(1, record.VersionNumber);
        Assert.Contains("FICTIF", record.DataJson, StringComparison.Ordinal);
        Assert.Null(await new BackOfficeStore(db).GetSubmissionAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Audit_entries_are_persisted()
    {
        var userId = Guid.NewGuid();
        await using (var db = fixture.CreateContext())
        {
            await new AuditLog(db, TimeProvider.System).RecordAsync(AuditActions.SubmissionViewed, userId, "submission:x", default);
        }

        await using (var db = fixture.CreateContext())
        {
            var entry = await db.AuditLog.SingleAsync(a => a.UserId == userId);
            Assert.Equal(AuditActions.SubmissionViewed, entry.Action);
            Assert.Equal("submission:x", entry.Target);
        }
    }
}
