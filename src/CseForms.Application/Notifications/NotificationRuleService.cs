using System.Globalization;
using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.Settings;
using CseForms.Domain.Common;
using CseForms.Domain.Notifications;

namespace CseForms.Application.Notifications;

/// <summary>
/// Règle de notification d'un formulaire depuis le back-office : destinataires, objet et corps du message
/// envoyé au CSE à chaque réponse. Mêmes droits que les réglages du formulaire (administrateurs, éditeurs
/// ayant le droit sur le formulaire). Le journal ne garde que le nombre de destinataires, jamais les adresses.
/// </summary>
public sealed class NotificationRuleService(
    IBackOfficeStore backOffice,
    INotificationRuleStore store,
    ISettingReader settings,
    IAuditLog audit,
    TimeProvider timeProvider)
{
    /// <summary>Séparateurs acceptés entre deux adresses : retour à la ligne, virgule, point-virgule.</summary>
    private static readonly char[] RecipientSeparators = ['\n', '\r', ',', ';'];

    public async Task<NotificationResult<NotificationPage>> GetPageAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var (status, form) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != NotificationStatus.Ok)
        {
            return new(status);
        }

        var rule = await store.FindRuleAsync(formId, cancellationToken);
        var values = await settings.GetAllAsync(cancellationToken);
        var baseUrl = values.TryGetValue(SettingKeys.PublicBaseUrl, out var url) && !string.IsNullOrWhiteSpace(url) ? url : null;

        return new(NotificationStatus.Ok, new NotificationPage(
            form!.Id,
            form.Slug,
            form.Title,
            rule is null ? null : new NotificationRuleView(rule.Recipients, rule.Subject, rule.Body),
            rule is null ? null : Preview(form, rule, baseUrl),
            MissingBaseUrl: baseUrl is null));
    }

    /// <param name="recipients">Adresses séparées par des retours à la ligne, des virgules ou des points-virgules.</param>
    public async Task<NotificationResult> SaveAsync(
        CurrentUser user,
        Guid formId,
        string? recipients,
        string? subject,
        string? body,
        CancellationToken cancellationToken)
    {
        var (status, _) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != NotificationStatus.Ok)
        {
            return new(status);
        }

        var unknown = NotificationTemplate.UnknownVariables(subject).Concat(NotificationTemplate.UnknownVariables(body)).Distinct().ToList();
        if (unknown.Count > 0)
        {
            return NotificationResult.Invalid(
                $"Variable(s) inconnue(s) : {Braced(unknown)}. Variables possibles : {Braced(NotificationTemplate.Variables)}.");
        }

        var addresses = (recipients ?? string.Empty).Split(RecipientSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var rule = await store.FindRuleAsync(formId, cancellationToken);
        try
        {
            if (rule is null)
            {
                rule = new NotificationRule(formId, addresses, subject, body);
                store.Add(rule);
            }
            else
            {
                rule.Update(addresses, subject, body);
            }
        }
        catch (DomainException ex)
        {
            return NotificationResult.Invalid(ex.Message);
        }

        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            AuditActions.FormNotificationsChanged,
            user.Id,
            Target(formId),
            $"recipients={rule.Recipients.Count.ToString(CultureInfo.InvariantCulture)}",
            cancellationToken);
        return NotificationResult.Ok;
    }

    /// <summary>Plus aucune notification pour ce formulaire ; l'accusé de réception au répondant n'est pas concerné.</summary>
    public async Task<NotificationResult> RemoveAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var (status, _) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != NotificationStatus.Ok)
        {
            return new(status);
        }

        var rule = await store.FindRuleAsync(formId, cancellationToken);
        if (rule is null)
        {
            return NotificationResult.Ok;
        }

        store.Remove(rule);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.FormNotificationsRemoved, user.Id, Target(formId), cancellationToken);
        return NotificationResult.Ok;
    }

    /// <summary>
    /// Message tel qu'il partirait, construit par le même code que l'envoi réel, avec un dossier fictif.
    /// Le site d'exemple n'est jamais une valeur saisie.
    /// </summary>
    private NotificationPreview Preview(NotificationForm form, NotificationRule rule, string? baseUrl)
    {
        var now = timeProvider.GetUtcNow();
        var source = new EmailSource(
            Guid.Empty,
            form.Title,
            $"{form.CaseNumberPrefix}-{CseCalendar.ToLocal(now).Year.ToString(CultureInfo.InvariantCulture)}-0042",
            now,
            PreviewSchema,
            """{"site":"Site d'exemple"}""",
            new NotificationRuleView(rule.Recipients, rule.Subject, rule.Body),
            new Dictionary<string, string> { [SettingKeys.PublicBaseUrl] = baseUrl ?? "https://adresse-publique-non-definie.invalid" });
        var message = SubmissionEmailService.BuildNotification(source)!;
        return new NotificationPreview(message.Subject, message.Body);
    }

    private const string PreviewSchema = """{"sections":[{"id":"s","fields":[{"id":"site","type":"site","label":"Site"}]}]}""";

    private async Task<(NotificationStatus Status, NotificationForm? Form)> AuthorizeAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var form = await store.GetFormAsync(formId, cancellationToken);
        if (form is null)
        {
            return (NotificationStatus.NotFound, null);
        }

        if (!await backOffice.CanAccessFormAsync(user, formId, cancellationToken))
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, Target(formId), cancellationToken);
            return (NotificationStatus.NotFound, null);
        }

        if (user.IsReadOnly)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, Target(formId), cancellationToken);
            return (NotificationStatus.Forbidden, null);
        }

        return (NotificationStatus.Ok, form);
    }

    private static string Target(Guid formId) => $"form:{formId}";

    private static string Braced(IEnumerable<string> names) => string.Join(", ", names.Select(n => $"{{{n}}}"));
}

public interface INotificationRuleStore
{
    Task<NotificationForm?> GetFormAsync(Guid formId, CancellationToken cancellationToken);

    /// <summary>Règle suivie pour modification ; null si le formulaire n'en a pas.</summary>
    Task<NotificationRule?> FindRuleAsync(Guid formId, CancellationToken cancellationToken);

    void Add(NotificationRule rule);

    void Remove(NotificationRule rule);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record NotificationForm(Guid Id, string Slug, string Title, string CaseNumberPrefix);

public sealed record NotificationPreview(string Subject, string Body);

/// <param name="Rule">Règle enregistrée ; null : aucune notification n'est envoyée.</param>
/// <param name="MissingBaseUrl">Sans adresse publique, le lien est impossible et chaque notification échoue.</param>
public sealed record NotificationPage(
    Guid FormId,
    string Slug,
    string Title,
    NotificationRuleView? Rule,
    NotificationPreview? Preview,
    bool MissingBaseUrl);

public enum NotificationStatus
{
    Ok,
    NotFound,
    Forbidden,
    Invalid,
}

public sealed record NotificationResult(NotificationStatus Status, string? Error = null)
{
    public static readonly NotificationResult Ok = new(NotificationStatus.Ok);

    public static NotificationResult Invalid(string error) => new(NotificationStatus.Invalid, error);
}

public sealed record NotificationResult<T>(NotificationStatus Status, T? Value = default);
