using System.Text.Json;
using CseForms.Application.BackOffice;
using CseForms.Domain.Forms;
using CseForms.Domain.Jobs;
using CseForms.Domain.Submissions;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.BackOffice;

internal sealed class BackOfficeStore(CseFormsDbContext db) : IBackOfficeStore
{
    public async Task<IReadOnlyList<FormSummary>> ListFormsAsync(CurrentUser user, CancellationToken cancellationToken)
    {
        var forms = db.Forms.AsNoTracking();
        if (!user.IsAdmin)
        {
            forms = forms.Where(f => db.UserFormAccess.Any(a => a.UserId == user.Id && a.FormId == f.Id));
        }

        return await forms
            .OrderBy(f => f.Title)
            .Select(f => new FormSummary(
                f.Id,
                f.Slug,
                f.Title,
                f.Status.ToString(),
                db.Submissions.Count(s => db.FormVersions.Any(v => v.Id == s.FormVersionId && v.FormId == f.Id))))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> CanAccessFormAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken) =>
        user.IsAdmin || await db.UserFormAccess.AnyAsync(a => a.UserId == user.Id && a.FormId == formId, cancellationToken);

    public async Task<FormHeader?> GetFormAsync(Guid formId, CancellationToken cancellationToken) =>
        await db.Forms
            .AsNoTracking()
            .Where(f => f.Id == formId)
            .Select(f => new FormHeader(
                f.Id,
                f.Slug,
                f.Title,
                db.FormVersions.Where(v => v.Id == f.CurrentVersionId).Select(v => v.SchemaJson).FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<SubmissionPage> ListSubmissionsAsync(SubmissionQuery query, CancellationToken cancellationToken)
    {
        var all = db.Submissions
            .AsNoTracking()
            .Where(s => db.FormVersions.Any(v => v.Id == s.FormVersionId && v.FormId == query.FormId));

        var statuses = await all.Select(s => s.Status).Distinct().OrderBy(s => s).ToListAsync(cancellationToken);

        var filtered = all;
        if (query.CaseNumberContains is { } search)
        {
            filtered = filtered.Where(s => s.CaseNumber.ToUpper().Contains(search));
        }

        if (query.Status is { } status)
        {
            filtered = filtered.Where(s => s.Status == status);
        }

        if (query.SiteFieldId is { } siteField && query.Site is { } site)
        {
            // Opérateur jsonb @> : la valeur est passée en paramètre, jamais concaténée.
            var contained = JsonSerializer.Serialize(new Dictionary<string, string> { [siteField] = site });
            filtered = filtered.Where(s => EF.Functions.JsonContains(s.DataJson, contained));
        }

        if (query.FromUtc is { } from)
        {
            filtered = filtered.Where(s => s.CreatedAt >= from);
        }

        if (query.ToUtcExclusive is { } to)
        {
            filtered = filtered.Where(s => s.CreatedAt < to);
        }

        var total = await filtered.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)SubmissionFilter.PageSize));
        var page = Math.Min(query.Page, pageCount);

        var ordered = query.OldestFirst
            ? filtered.OrderBy(s => s.CreatedAt).ThenBy(s => s.CaseNumber)
            : filtered.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.CaseNumber);

        var rows = await ordered
            .Skip((page - 1) * SubmissionFilter.PageSize)
            .Take(SubmissionFilter.PageSize)
            .Select(s => new { s.Id, s.CaseNumber, s.CreatedAt, s.Status, s.DataJson })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new SubmissionListItem(r.Id, r.CaseNumber, r.CreatedAt, r.Status, SiteOf(r.DataJson, query.SiteFieldId)))
            .ToList();

        return new SubmissionPage(items, total, page, pageCount, statuses);
    }

    public async Task<SubmissionRecord?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) =>
        await (
            from s in db.Submissions.AsNoTracking()
            join v in db.FormVersions on s.FormVersionId equals v.Id
            join f in db.Forms on v.FormId equals f.Id
            where s.Id == submissionId
            select new SubmissionRecord(
                s.Id, f.Id, f.Title, s.CaseNumber, v.Number, v.SchemaJson, s.DataJson, s.Status, s.CreatedAt, s.PurgeAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PdfState> GetPdfStateAsync(Guid submissionId, CancellationToken cancellationToken)
    {
        var generatedAt = await db.SubmissionFiles.AsNoTracking()
            .Where(f => f.SubmissionId == submissionId && f.Kind == SubmissionFileKind.Pdf)
            .Select(f => (DateTimeOffset?)f.CreatedAt)
            .SingleOrDefaultAsync(cancellationToken);

        var lastStatus = await db.Jobs.AsNoTracking()
            .Where(j => j.SubmissionId == submissionId && j.Type == JobType.Pdf)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => (JobStatus?)j.Status)
            .FirstOrDefaultAsync(cancellationToken);

        return new PdfState(
            generatedAt,
            Generating: lastStatus is JobStatus.Pending or JobStatus.Queued,
            Failed: lastStatus is JobStatus.Failed);
    }

    public Task<StoredPdf?> GetPdfFileAsync(Guid submissionId, CancellationToken cancellationToken) =>
        db.SubmissionFiles.AsNoTracking()
            .Where(f => f.SubmissionId == submissionId && f.Kind == SubmissionFileKind.Pdf)
            .Select(f => new StoredPdf(f.Id, f.StoragePath))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> RequestPdfAsync(Guid submissionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await db.Jobs.AnyAsync(
            j => j.SubmissionId == submissionId
                && j.Type == JobType.Pdf
                && (j.Status == JobStatus.Pending || j.Status == JobStatus.Queued),
            cancellationToken);
        if (pending)
        {
            return false;
        }

        db.Jobs.Add(new SubmissionJob(submissionId, JobType.Pdf, now));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? SiteOf(string dataJson, string? siteFieldId)
    {
        if (siteFieldId is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(dataJson);
        return document.RootElement.TryGetProperty(siteFieldId, out var site) && site.ValueKind == JsonValueKind.String
            ? site.GetString()
            : null;
    }
}
