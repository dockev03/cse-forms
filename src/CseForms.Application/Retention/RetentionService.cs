using CseForms.Application.BackOffice;
using Microsoft.Extensions.Logging;

namespace CseForms.Application.Retention;

/// <summary>
/// Purge automatique : supprime les réponses dont la durée de conservation est écoulée
/// (comptée à partir de leur traitement). Chaque suppression est tracée, sans auteur.
/// </summary>
public sealed partial class RetentionService(
    ISubmissionEraseStore store,
    SubmissionEraser eraser,
    IAuditLog audit,
    TimeProvider timeProvider,
    ILogger<RetentionService> logger)
{
    public const int BatchSize = 100;

    public async Task<int> PurgeDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var purged = 0;

        while (true)
        {
            var due = await store.ListDueAsync(now, BatchSize, cancellationToken);
            var purgedInBatch = 0;

            foreach (var submission in due)
            {
                if (await eraser.EraseAsync(submission.Id, now, cancellationToken))
                {
                    await audit.RecordAsync(AuditActions.SubmissionPurged, null, $"submission:{submission.Id}", submission.CaseNumber, cancellationToken);
                    purgedInBatch++;
                }
            }

            purged += purgedInBatch;

            // Lot incomplet ou rien de supprimable (statuts changés entre-temps) : terminé.
            if (due.Count < BatchSize || purgedInBatch == 0)
            {
                break;
            }
        }

        LogPurged(purged);
        return purged;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purge : {Count} réponse(s) supprimée(s)")]
    private partial void LogPurged(int count);
}
