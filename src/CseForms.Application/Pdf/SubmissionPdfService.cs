using CseForms.Domain.Submissions;
using Microsoft.Extensions.Logging;

namespace CseForms.Application.Pdf;

/// <summary>
/// Exécute une tâche de génération de PDF : rendu HTML, conversion, stockage chiffré, puis
/// remplacement du PDF précédent. La tâche est rejouable sans effet de bord.
/// </summary>
public sealed partial class SubmissionPdfService(
    ISubmissionPdfStore store,
    IFileVault vault,
    IPdfConverter converter,
    TimeProvider timeProvider,
    ILogger<SubmissionPdfService> logger)
{
    public async Task<JobRunResult> RunJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await store.FindJobAsync(jobId, cancellationToken);
        if (job is null || job.IsFinished)
        {
            return JobRunResult.Done;
        }

        StoredFile? written = null;
        var committed = false;
        try
        {
            var source = await store.GetSourceAsync(job.SubmissionId, cancellationToken)
                ?? throw new InvalidOperationException("Réponse introuvable.");

            var pdf = await converter.ConvertAsync(SubmissionPdfRenderer.Render(source), cancellationToken);
            var fileId = Guid.CreateVersion7();
            written = await vault.WriteAsync(fileId, source.SubmissionId, pdf, cancellationToken);

            var now = timeProvider.GetUtcNow();
            var previous = await store.ReplacePdfAsync(
                new SubmissionFile(fileId, source.SubmissionId, SubmissionFileKind.Pdf, written.StoragePath, written.Sha256, written.SizeBytes, now),
                cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            committed = true;

            foreach (var path in previous)
            {
                vault.Delete(path);
            }

            job.MarkSucceeded(now);
            await store.SaveChangesAsync(cancellationToken);

            LogSucceeded(job.Id, job.SubmissionId);
            return JobRunResult.Done;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (committed)
            {
                // Le PDF est enregistré ; seul l'état de la tâche n'a pas pu l'être : nouvel essai sans effet de bord.
                LogRetry(ex, job.Id, job.SubmissionId, job.Attempts);
                return JobRunResult.Retry;
            }

            // Le fichier écrit mais non enregistré en base ne doit pas rester sur le disque.
            if (written is not null)
            {
                vault.Delete(written.StoragePath);
            }

            store.DiscardFileChanges();

            // Seul le type d'erreur est conservé : un message pourrait contenir une donnée saisie.
            var retry = job.RecordFailure(ex.GetType().Name, timeProvider.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken);

            if (retry)
            {
                LogRetry(ex, job.Id, job.SubmissionId, job.Attempts);
                return JobRunResult.Retry;
            }

            LogFailed(ex, job.Id, job.SubmissionId, job.Attempts);
            return JobRunResult.Done;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "PDF généré pour la réponse {SubmissionId} (tâche {JobId})")]
    private partial void LogSucceeded(Guid jobId, Guid submissionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec de la tâche {JobId} (réponse {SubmissionId}), essai {Attempt} : nouvel essai prévu")]
    private partial void LogRetry(Exception exception, Guid jobId, Guid submissionId, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec définitif de la tâche {JobId} (réponse {SubmissionId}) après {Attempt} essais")]
    private partial void LogFailed(Exception exception, Guid jobId, Guid submissionId, int attempt);
}

public enum JobRunResult
{
    Done,

    /// <summary>Essai manqué : la file de tâches doit relancer la tâche.</summary>
    Retry,
}
