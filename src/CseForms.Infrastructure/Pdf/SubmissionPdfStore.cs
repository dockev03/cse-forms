using CseForms.Application.Pdf;
using CseForms.Domain.Jobs;
using CseForms.Domain.Submissions;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Pdf;

internal sealed class SubmissionPdfStore(CseFormsDbContext db) : ISubmissionPdfStore
{
    public Task<SubmissionJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);

    public async Task<PdfSource?> GetSourceAsync(Guid submissionId, CancellationToken cancellationToken)
    {
        var row = await (
            from s in db.Submissions.AsNoTracking()
            join v in db.FormVersions on s.FormVersionId equals v.Id
            join f in db.Forms on v.FormId equals f.Id
            where s.Id == submissionId
            select new { s.Id, f.Title, s.CaseNumber, v.Number, s.CreatedAt, v.SchemaJson, s.DataJson })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var settings = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        return new PdfSource(row.Id, row.Title, row.CaseNumber, row.Number, row.CreatedAt, row.SchemaJson, row.DataJson, settings);
    }

    public async Task<IReadOnlyList<string>> ReplacePdfAsync(SubmissionFile file, CancellationToken cancellationToken)
    {
        var previous = await db.SubmissionFiles
            .Where(f => f.SubmissionId == file.SubmissionId && f.Kind == file.Kind)
            .ToListAsync(cancellationToken);

        db.SubmissionFiles.RemoveRange(previous);
        db.SubmissionFiles.Add(file);
        return previous.Select(f => f.StoragePath).ToList();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public void DiscardFileChanges()
    {
        foreach (var entry in db.ChangeTracker.Entries<SubmissionFile>().ToList())
        {
            entry.State = entry.State == EntityState.Added ? EntityState.Detached : EntityState.Unchanged;
        }
    }
}
