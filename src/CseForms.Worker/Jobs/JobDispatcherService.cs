using CseForms.Domain.Jobs;
using CseForms.Infrastructure.Jobs;
using Hangfire;

namespace CseForms.Worker.Jobs;

/// <summary>Relève régulièrement les tâches enregistrées avec les réponses et les confie à Hangfire.</summary>
public sealed partial class JobDispatcherService(
    IServiceScopeFactory scopeFactory,
    IBackgroundJobClient jobs,
    IConfiguration configuration,
    ILogger<JobDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = Math.Clamp(configuration.GetValue("Jobs:PollSeconds", 5), 1, 300);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<PendingJobDispatcher>();
                var count = await dispatcher.DispatchAsync(Enqueue, stoppingToken);
                if (count > 0)
                {
                    LogDispatched(count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Base momentanément indisponible : nouvel essai au prochain passage.
                LogDispatchFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private void Enqueue(SubmissionJob job)
    {
        switch (job.Type)
        {
            case JobType.Pdf:
                jobs.Enqueue<PdfJob>(j => j.RunAsync(job.Id, CancellationToken.None));
                break;
            default:
                throw new InvalidOperationException($"Type de tâche inconnu : {job.Type}.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} tâche(s) confiée(s) à la file")]
    private partial void LogDispatched(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Relève des tâches impossible, nouvel essai au prochain passage")]
    private partial void LogDispatchFailed(Exception exception);
}
