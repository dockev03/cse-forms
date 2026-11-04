using CseForms.Application.Pdf;
using CseForms.Domain.Jobs;
using Hangfire;

namespace CseForms.Worker.Jobs;

/// <summary>
/// Tâche Hangfire de génération du PDF. Le nombre d'essais est tenu dans la table <c>job</c> :
/// Hangfire relance la tâche tant que le service demande un nouvel essai, soit 3 essais au plus.
/// </summary>
public sealed class PdfJob(SubmissionPdfService service)
{
    [AutomaticRetry(Attempts = SubmissionJob.MaxAttempts - 1, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (await service.RunJobAsync(jobId, cancellationToken) == JobRunResult.Retry)
        {
            throw new JobRetryException(jobId);
        }
    }
}

/// <summary>Signale à Hangfire qu'un nouvel essai est nécessaire ; le détail est déjà journalisé.</summary>
public sealed class JobRetryException(Guid jobId) : Exception($"Nouvel essai demandé pour la tâche {jobId}.");
