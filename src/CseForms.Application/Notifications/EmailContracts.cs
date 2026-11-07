namespace CseForms.Application.Notifications;

/// <summary>Envoi d'un email en texte brut. Les réglages SMTP sont lus dans les paramètres à chaque envoi.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Message en texte brut : aucun HTML, donc rien d'interprété chez le destinataire.</summary>
/// <param name="ReplyTo">Adresse de réponse (contact du CSE) ; null : aucune.</param>
public sealed record EmailMessage(IReadOnlyList<string> To, string Subject, string Body, string? ReplyTo = null);

/// <summary>Réglage SMTP absent ou invalide : la tâche échoue et sera relancée.</summary>
public sealed class EmailNotConfiguredException(string message) : Exception(message);

/// <summary>Lecture des paramètres de l'application (table <c>setting</c>).</summary>
public interface ISettingReader
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken);
}
