using CseForms.Application.Retention;
using Hangfire;

namespace CseForms.Worker.Jobs;

/// <summary>
/// Purge quotidienne des réponses dont la durée de conservation est écoulée. Planifiée au démarrage
/// du Worker ; en cas d'échec, la purge suivante reprend où elle s'est arrêtée.
/// </summary>
public sealed class PurgeJob(RetentionService service)
{
    public const string RecurringJobId = "purge-reponses";

    /// <summary>Tous les jours à 3 h 17, heure de Paris : hors heures d'utilisation.</summary>
    public const string DefaultCron = "17 3 * * *";

    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public Task RunAsync(CancellationToken cancellationToken) => service.PurgeDueAsync(cancellationToken);

    public static void Schedule(IRecurringJobManager manager, IConfiguration configuration) =>
        manager.AddOrUpdate<PurgeJob>(
            RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            configuration["Purge:Cron"] is { Length: > 0 } cron ? cron : DefaultCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris") });
}
