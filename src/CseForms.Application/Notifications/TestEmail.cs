using CseForms.Application.Settings;

namespace CseForms.Application.Notifications;

/// <summary>Email de test des réglages SMTP : il ne contient que la configuration, jamais d'identifiant ni de donnée.</summary>
public static class TestEmail
{
    public static EmailMessage Build(string to, IReadOnlyDictionary<string, string> settings, DateTimeOffset now)
    {
        string Setting(string key, string fallback) =>
            settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

        var body = $"""
            Ce message confirme que l'outil de formulaires du CSE peut envoyer des emails.

            Serveur : {Setting(SettingKeys.SmtpHost, "(non défini)")}, port {Setting(SettingKeys.SmtpPort, "587")}, chiffrement {Setting(SettingKeys.SmtpSecurity, "starttls")}
            Expéditeur : {Setting(SettingKeys.SmtpFrom, "(non défini)")}
            Envoyé le : {Common.CseCalendar.ToLocal(now):dd/MM/yyyy 'à' HH:mm}

            Aucune action n'est nécessaire.
            """;

        return new EmailMessage([to], "[Formulaires CSE] Email de test", body);
    }
}
