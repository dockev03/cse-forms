using System.Net.Mail;
using CseForms.Domain.Common;

namespace CseForms.Domain.Notifications;

/// <summary>
/// Règle de notification d'un formulaire (table <c>notification_rule</c>) : destinataires, objet et corps
/// du message envoyé au CSE à chaque réponse. Réglée en base, hors de la définition versionnée, pour changer
/// une adresse sans publier de version et ne jamais versionner d'adresse dans le dépôt.
/// </summary>
public sealed class NotificationRule
{
    public const int MaxRecipients = 20;
    public const int RecipientMaxLength = 254;
    public const int SubjectMaxLength = 200;
    public const int BodyMaxLength = 4000;

    private List<string> _recipients = [];

    private NotificationRule()
    {
    }

    public NotificationRule(Guid formId, IEnumerable<string> recipients, string? subject, string? body)
    {
        Id = Guid.CreateVersion7();
        FormId = formId;
        Update(recipients, subject, body);
    }

    public Guid Id { get; private set; }

    public Guid FormId { get; private set; }

    /// <summary>Adresses en minuscules, sans doublon, dans l'ordre de saisie.</summary>
    public IReadOnlyList<string> Recipients => _recipients;

    /// <summary>Objet propre au formulaire ; null : objet par défaut.</summary>
    public string? Subject { get; private set; }

    /// <summary>Corps propre au formulaire ; null : corps par défaut.</summary>
    public string? Body { get; private set; }

    public void Update(IEnumerable<string> recipients, string? subject, string? body)
    {
        var list = new List<string>();
        foreach (var raw in recipients ?? [])
        {
            var address = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (address.Length == 0)
            {
                continue;
            }

            if (address.Length > RecipientMaxLength
                || !MailAddress.TryCreate(address, out var parsed)
                || parsed.Address != address
                || !string.IsNullOrEmpty(parsed.DisplayName))
            {
                throw new DomainException("Adresse de destinataire invalide.");
            }

            if (!list.Contains(address))
            {
                list.Add(address);
            }
        }

        if (list.Count == 0 || list.Count > MaxRecipients)
        {
            throw new DomainException($"De 1 à {MaxRecipients} destinataires attendus.");
        }

        _recipients = list;
        Subject = Optional(subject, SubjectMaxLength, singleLine: true, "L'objet");
        Body = Optional(body, BodyMaxLength, singleLine: false, "Le corps");
    }

    private static string? Optional(string? value, int maxLength, bool singleLine, string name)
    {
        var text = value?.ReplaceLineEndings("\n").Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > maxLength)
        {
            throw new DomainException($"{name} est limité à {maxLength} caractères.");
        }

        // Un retour à la ligne dans l'objet permettrait d'injecter des en-têtes.
        if (singleLine && text.Contains('\n'))
        {
            throw new DomainException($"{name} tient sur une seule ligne.");
        }

        return text;
    }
}
