using CseForms.Domain.Jobs;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Jobs;

/// <summary>
/// Relève les tâches enregistrées avec les réponses et les confie à la file de tâches.
/// Le verrou <c>SKIP LOCKED</c> permet plusieurs Workers sans qu'une tâche soit confiée deux fois.
/// </summary>
public sealed class PendingJobDispatcher(CseFormsDbContext db, TimeProvider timeProvider)
{
    public const int BatchSize = 50;

    /// <summary>Confie les tâches en attente à <paramref name="enqueue"/> ; renvoie leur nombre.</summary>
    public async Task<int> DispatchAsync(Action<SubmissionJob> enqueue, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var jobs = await db.Jobs
            .FromSql($"""
                SELECT * FROM job
                WHERE status = 'Pending'
                ORDER BY created_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            enqueue(job);
            job.MarkQueued(timeProvider.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return jobs.Count;
    }
}
