using CseForms.Application.Notifications;
using CseForms.Application.Pdf;
using CseForms.Domain.Jobs;
using Hangfire;

namespace CseForms.Worker.Jobs;

/// <summary>
/// Tâche Hangfire d'envoi d'email (notification au CSE ou accusé de réception). Comme pour le PDF,
/// le nombre d'essais est tenu dans la table <c>job</c> : 3 essais au plus, puis alerte à l'administrateur.
/// </summary>
public sealed class EmailJob(SubmissionEmailService service)
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
