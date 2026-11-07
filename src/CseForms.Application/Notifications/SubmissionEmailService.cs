using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.FormDefinitions;
using CseForms.Application.Jobs;
using CseForms.Application.Pdf;
using CseForms.Application.Settings;
using CseForms.Domain.Jobs;
using Microsoft.Extensions.Logging;

namespace CseForms.Application.Notifications;

/// <summary>
/// Exécute une tâche email liée à une réponse : notification au CSE ou accusé de réception au répondant.
/// Aucun des deux messages ne contient de donnée saisie : seuls le numéro de dossier, le formulaire,
/// la date, le site et un lien vers le back-office (qui exige une connexion) y figurent.
/// </summary>
public sealed partial class SubmissionEmailService(
    ISubmissionEmailStore store,
    IEmailSender sender,
    IJobFailureAlerter alerter,
    TimeProvider timeProvider,
    ILogger<SubmissionEmailService> logger)
{
    public async Task<JobRunResult> RunJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await store.FindJobAsync(jobId, cancellationToken);
        if (job is null || job.IsFinished)
        {
            return JobRunResult.Done;
        }

        var source = await store.GetSourceAsync(job.SubmissionId, cancellationToken);
        try
        {
            if (source is null)
            {
                throw new InvalidOperationException("Réponse introuvable.");
            }

            var message = job.Type switch
            {
                JobType.Notification => BuildNotification(source),
                JobType.Receipt => BuildReceipt(source),
                _ => throw new InvalidOperationException("Type de tâche inattendu."),
            };

            if (message is null)
            {
                LogNothingToSend(job.Id, job.SubmissionId, job.Type);
            }
            else
            {
                await sender.SendAsync(message, cancellationToken);
                LogSent(job.Id, job.SubmissionId, job.Type);
            }

            // Envoyé : la tâche est close tout de suite, pour ne pas renvoyer le message à un nouvel essai.
            job.MarkSucceeded(timeProvider.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken);
            return JobRunResult.Done;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Seul le type d'erreur est conservé : un message d'erreur SMTP peut citer une adresse.
            var retry = job.RecordFailure(ex.GetType().Name, timeProvider.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken);

            if (retry)
            {
                LogRetry(ex.GetType().Name, job.Id, job.SubmissionId, job.Attempts);
                return JobRunResult.Retry;
            }

            LogFailed(ex.GetType().Name, job.Id, job.SubmissionId, job.Attempts);
            await alerter.AlertAsync(new JobFailure(job.Id, job.Type, job.SubmissionId, source?.CaseNumber, job.LastError), cancellationToken);
            return JobRunResult.Done;
        }
    }

    /// <summary>Notification au CSE ; null si le formulaire n'a pas de règle de notification.</summary>
    public static EmailMessage? BuildNotification(EmailSource source)
    {
        if (source.Rule is null)
        {
            return null;
        }

        var values = Values(source);
        var subject = NotificationTemplate.Render(source.Rule.Subject ?? NotificationTemplate.DefaultSubject, values);

        string body;
        if (source.Rule.Body is { } custom)
        {
            body = NotificationTemplate.Render(custom, values);
            if (!custom.Contains("{lien}", StringComparison.Ordinal))
            {
                body += $"\n\nConsulter la réponse (connexion requise) :\n{values["lien"]}";
            }
        }
        else
        {
            var text = new StringBuilder()
                .Append("Une nouvelle réponse a été déposée.\n\n")
                .Append("Formulaire : ").Append(values["formulaire"]).Append('\n')
                .Append("Dossier : ").Append(values["numero"]).Append('\n')
                .Append("Date : ").Append(values["date"]).Append('\n');
            if (values["site"].Length > 0)
            {
                text.Append("Site : ").Append(values["site"]).Append('\n');
            }

            text.Append("\nConsulter la réponse (connexion requise) :\n").Append(values["lien"]).Append('\n')
                .Append("\nCe message ne contient aucune donnée personnelle : les informations saisies restent dans le back-office.");
            body = text.ToString();
        }

        return new EmailMessage(source.Rule.Recipients, OneLine(subject), body);
    }

    /// <summary>Accusé de réception ; null si le formulaire n'en envoie pas ou si le répondant n'a pas donné d'email.</summary>
    public static EmailMessage? BuildReceipt(EmailSource source)
    {
        var receipt = ReceiptSettings.Read(source.SchemaJson);
        if (receipt is null)
        {
            return null;
        }

        var data = JsonNode.Parse(source.DataJson) as JsonObject;
        var address = data?[receipt.EmailField] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        if (!SettingKeys.IsPlainEmail(address))
        {
            return null;
        }

        string? Setting(string key) => source.Settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        var local = CseCalendar.ToLocal(source.SubmittedAt);
        var cse = Setting(SettingKeys.CseName) ?? "CSE";

        var body = new StringBuilder()
            .Append("Bonjour,\n\n")
            .Append(cse).Append(" a bien reçu votre formulaire « ").Append(source.FormTitle).Append(" »")
            .Append(" le ").Append(local.ToString("dd/MM/yyyy 'à' HH:mm", CultureInfo.InvariantCulture)).Append(".\n")
            .Append("Numéro de dossier : ").Append(source.CaseNumber).Append("\n\n");

        if (receipt.Message is not null)
        {
            body.Append(receipt.Message).Append("\n\n");
        }

        var contact = new[] { Setting(SettingKeys.CseEmail), Setting(SettingKeys.CsePhone) }.Where(c => c is not null).ToList();
        if (contact.Count > 0)
        {
            body.Append("Contact : ").Append(string.Join(" ; ", contact)).Append("\n\n");
        }

        body.Append("Ce message est envoyé automatiquement. Par sécurité, il ne reprend aucune des informations saisies.");

        return new EmailMessage(
            [address!],
            OneLine($"Accusé de réception : dossier n° {source.CaseNumber}"),
            body.ToString(),
            Setting(SettingKeys.CseEmail));
    }

    private static Dictionary<string, string> Values(EmailSource source)
    {
        var baseUrl = source.Settings.TryGetValue(SettingKeys.PublicBaseUrl, out var url) && !string.IsNullOrWhiteSpace(url)
            ? url.TrimEnd('/')
            : throw new EmailNotConfiguredException("Adresse publique du site absente (app.publicBaseUrl) : lien impossible.");

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["formulaire"] = source.FormTitle,
            ["numero"] = source.CaseNumber,
            ["date"] = CseCalendar.ToLocal(source.SubmittedAt).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["site"] = SiteOf(source) ?? string.Empty,
            ["lien"] = $"{baseUrl}/admin/reponses/{source.SubmissionId}",
        };
    }

    /// <summary>Premier champ « site » renseigné de la version remplie.</summary>
    private static string? SiteOf(EmailSource source)
    {
        var parsed = FormDefinitionParser.Parse(source.SchemaJson);
        if (!parsed.IsValid || JsonNode.Parse(source.DataJson) is not JsonObject data)
        {
            return null;
        }

        foreach (var field in parsed.Definition!.AllTopLevelFields.Where(f => f.Type == FieldType.Site))
        {
            if (data[field.Id] is JsonValue value && value.TryGetValue<string>(out var site) && !string.IsNullOrWhiteSpace(site))
            {
                return site;
            }
        }

        return null;
    }

    /// <summary>Un objet ne contient jamais de retour à la ligne, qui permettrait d'injecter un en-tête.</summary>
    private static string OneLine(string text) => string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {Type} envoyé pour la réponse {SubmissionId} (tâche {JobId})")]
    private partial void LogSent(Guid jobId, Guid submissionId, JobType type);

    [LoggerMessage(Level = LogLevel.Information, Message = "Aucun email {Type} à envoyer pour la réponse {SubmissionId} (tâche {JobId})")]
    private partial void LogNothingToSend(Guid jobId, Guid submissionId, JobType type);

    // Le type d'erreur seulement : le message d'une exception SMTP peut contenir une adresse.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec de la tâche {JobId} (réponse {SubmissionId}), essai {Attempt} : {Error}, nouvel essai prévu")]
    private partial void LogRetry(string error, Guid jobId, Guid submissionId, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec définitif de la tâche {JobId} (réponse {SubmissionId}) après {Attempt} essais : {Error}")]
    private partial void LogFailed(string error, Guid jobId, Guid submissionId, int attempt);
}

public interface ISubmissionEmailStore
{
    /// <summary>Tâche suivie pour modification ; null si elle n'existe plus (réponse supprimée).</summary>
    Task<SubmissionJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken);

    Task<EmailSource?> GetSourceAsync(Guid submissionId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Ce dont les emails ont besoin : la réponse, la version remplie, la règle du formulaire et les paramètres.</summary>
public sealed record EmailSource(
    Guid SubmissionId,
    string FormTitle,
    string CaseNumber,
    DateTimeOffset SubmittedAt,
    string SchemaJson,
    string DataJson,
    NotificationRuleView? Rule,
    IReadOnlyDictionary<string, string> Settings);

public sealed record NotificationRuleView(IReadOnlyList<string> Recipients, string? Subject, string? Body);
