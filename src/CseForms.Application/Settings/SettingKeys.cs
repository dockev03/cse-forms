using Logo = CseForms.Application.Settings.CseLogo;

namespace CseForms.Application.Settings;

/// <summary>
/// Paramètres connus de l'application (table <c>setting</c>). Une clé absente de cette liste est refusée,
/// pour qu'une faute de frappe ne passe pas inaperçue. Aucun secret ici : les mots de passe
/// (SMTP par exemple) restent en variables d'environnement.
/// </summary>
public static class SettingKeys
{
    public const string CseName = "cse.name";
    public const string CseAddress = "cse.address";
    public const string CseEmail = "cse.email";
    public const string CsePhone = "cse.phone";
    public const string CseOffice = "cse.office";
    public const string CseLogo = "cse.logo";
    public const string PublicBaseUrl = "app.publicBaseUrl";
    public const string SmtpHost = "smtp.host";
    public const string SmtpPort = "smtp.port";
    public const string SmtpSecurity = "smtp.security";
    public const string SmtpFrom = "smtp.from";
    public const string AlertsEmail = "alerts.email";

    /// <summary>Modes de chiffrement SMTP acceptés.</summary>
    public static readonly IReadOnlyList<string> SmtpSecurityModes = ["starttls", "ssl", "none"];

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [CseName] = "Nom du CSE, affiché sur les PDF, le flyer et les emails",
        [CseAddress] = "Adresse postale du CSE (plusieurs lignes possibles)",
        [CseEmail] = "Email de contact du CSE, communiqué aux salariés",
        [CsePhone] = "Téléphone de contact du CSE",
        [CseOffice] = "Composition du bureau : une ligne par membre (fonction : prénom nom)",
        [CseLogo] = "Logo du CSE (PDF, flyer) : se règle avec « settings logo --file <image> »",
        [PublicBaseUrl] = "Adresse publique du site, par exemple https://formulaires.example.test (QR code, liens)",
        [SmtpHost] = "Serveur SMTP d'envoi des emails (identifiant et mot de passe : variables Smtp__Username et Smtp__Password)",
        [SmtpPort] = "Port du serveur SMTP (587 par défaut)",
        [SmtpSecurity] = "Chiffrement SMTP : starttls (par défaut), ssl ou none (sans authentification)",
        [SmtpFrom] = "Adresse d'expédition des emails",
        [AlertsEmail] = "Adresse qui reçoit les alertes d'échec définitif des tâches (PDF, emails)",
    };

    public static bool IsKnown(string key) => Descriptions.ContainsKey(key);

    /// <summary>Adresse seule, sans nom affiché ni espace : rien qui puisse altérer un en-tête d'email.</summary>
    public static bool IsPlainEmail(string? value) =>
        value is { Length: > 0 and <= 254 }
        && System.Net.Mail.MailAddress.TryCreate(value, out var address)
        && address.Address == value
        && string.IsNullOrEmpty(address.DisplayName);

    /// <summary>Contrôle propre à certaines clés ; null si la valeur est acceptée.</summary>
    public static string? Validate(string key, string value)
    {
        if (key == PublicBaseUrl
            && (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || uri.AbsolutePath != "/"))
        {
            return "Adresse HTTPS attendue, sans chemin ni paramètre : https://formulaires.example.test";
        }

        if (key == CseLogo && !Logo.IsValid(value))
        {
            return $"Logo attendu : {Logo.Rule}.";
        }

        if ((key is CseEmail or SmtpFrom or AlertsEmail) && !IsPlainEmail(value))
        {
            return "Adresse email invalide.";
        }

        if (key == SmtpPort && !(int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535))
        {
            return "Port attendu : nombre entre 1 et 65535.";
        }

        if (key == SmtpSecurity && !SmtpSecurityModes.Contains(value))
        {
            return $"Valeur attendue : {string.Join(", ", SmtpSecurityModes)}.";
        }

        if (key == SmtpHost && Uri.CheckHostName(value) is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
        {
            return "Nom de serveur attendu, sans protocole ni port : smtp.example.test.";
        }

        return null;
    }
}
