using CseForms.Application.Notifications;
using CseForms.Application.Settings;
using CseForms.Domain.Jobs;
using Microsoft.Extensions.Logging;

namespace CseForms.Application.Jobs;

/// <summary>Tâche en échec définitif : identifiants et cause technique seulement, jamais une donnée saisie.</summary>
public sealed record JobFailure(Guid JobId, JobType Type, Guid SubmissionId, string? CaseNumber, string? Error);

/// <summary>Prévient l'administrateur qu'une tâche a échoué définitivement.</summary>
public interface IJobFailureAlerter
{
    /// <summary>Ne lève jamais d'exception : un échec de l'alerte est seulement journalisé.</summary>
    Task AlertAsync(JobFailure failure, CancellationToken cancellationToken);
}

/// <summary>
/// Alerte par email à l'adresse du paramètre <c>alerts.email</c>. Envoyée directement, hors file de tâches :
/// une alerte en échec ne doit pas déclencher une autre alerte.
/// </summary>
public sealed partial class EmailJobFailureAlerter(
    ISettingReader settings,
    IEmailSender sender,
    ILogger<EmailJobFailureAlerter> logger) : IJobFailureAlerter
{
    public async Task AlertAsync(JobFailure failure, CancellationToken cancellationToken)
    {
        try
        {
            var values = await settings.GetAllAsync(cancellationToken);
            if (!values.TryGetValue(SettingKeys.AlertsEmail, out var to) || !SettingKeys.IsPlainEmail(to))
            {
                LogNoRecipient(failure.JobId);
                return;
            }

            await sender.SendAsync(Build(failure, values), cancellationToken);
            LogAlertSent(failure.JobId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAlertFailed(ex.GetType().Name, failure.JobId);
        }
    }

    public static EmailMessage Build(JobFailure failure, IReadOnlyDictionary<string, string> settings)
    {
        var what = failure.Type switch
        {
            JobType.Pdf => "génération du PDF",
            JobType.Notification => "notification au CSE",
            JobType.Receipt => "accusé de réception au répondant",
            _ => failure.Type.ToString(),
        };

        var link = settings.TryGetValue(SettingKeys.PublicBaseUrl, out var url) && !string.IsNullOrWhiteSpace(url)
            ? $"{url.TrimEnd('/')}/admin/reponses/{failure.SubmissionId}"
            : $"réponse {failure.SubmissionId}";

        var body = $"""
            Une tâche de fond a échoué après {SubmissionJob.MaxAttempts} essais.

            Tâche : {what}
            Dossier : {failure.CaseNumber ?? "inconnu (réponse supprimée ?)"}
            Cause technique : {failure.Error ?? "inconnue"}
            Réponse : {link}

            La réponse est bien enregistrée. Après correction (paramètres SMTP, Gotenberg...), le PDF peut être
            régénéré depuis la fiche de la réponse ; les journaux du Worker donnent le détail (identifiant de tâche {failure.JobId}).
            """;

        return new EmailMessage([settings[SettingKeys.AlertsEmail]], $"[Formulaires CSE] Échec définitif : {what}", body);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alerte non envoyée pour la tâche {JobId} : paramètre alerts.email absent")]
    private partial void LogNoRecipient(Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Alerte envoyée pour la tâche {JobId}")]
    private partial void LogAlertSent(Guid jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Alerte impossible pour la tâche {JobId} : {Error}")]
    private partial void LogAlertFailed(string error, Guid jobId);
}
