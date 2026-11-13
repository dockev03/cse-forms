using CseForms.Application.Retention;
using CseForms.Domain.Submissions;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Retention;

internal sealed class SubmissionEraseStore(CseFormsDbContext db) : ISubmissionEraseStore
{
    public async Task<IReadOnlyList<DueSubmission>> ListDueAsync(DateTimeOffset now, int max, CancellationToken cancellationToken) =>
        await db.Submissions.AsNoTracking()
            .Where(s => s.PurgeAt != null && s.PurgeAt <= now)
            .OrderBy(s => s.PurgeAt)
            .Take(max)
            .Select(s => new DueSubmission(s.Id, s.CaseNumber))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>?> DeleteAsync(Guid submissionId, DateTimeOffset? dueAt, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Verrou sur la réponse : un PDF en cours d'enregistrement (clé étrangère) attend la fin de la suppression,
        // puis échoue proprement ; un changement de statut concurrent est vu avant de décider.
        var locked = await db.Submissions
            .FromSql($"""
                SELECT * FROM submission
                WHERE id = {submissionId}
                FOR UPDATE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (locked is null || (dueAt is { } due && (locked.PurgeAt is null || locked.PurgeAt > due)))
        {
            return null;
        }

        var paths = await db.SubmissionFiles.AsNoTracking()
            .Where(f => f.SubmissionId == submissionId)
            .Select(f => f.StoragePath)
            .ToListAsync(cancellationToken);

        // Fichiers et tâches suivent par ON DELETE CASCADE.
        await db.Submissions.Where(s => s.Id == submissionId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return paths;
    }
}
