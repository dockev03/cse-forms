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

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [CseName] = "Nom du CSE, affiché sur les PDF, le flyer et les emails",
        [CseAddress] = "Adresse postale du CSE (plusieurs lignes possibles)",
        [CseEmail] = "Email de contact du CSE, communiqué aux salariés",
        [CsePhone] = "Téléphone de contact du CSE",
        [CseOffice] = "Composition du bureau : une ligne par membre (fonction : prénom nom)",
        [CseLogo] = "Logo du CSE (PDF, flyer) : se règle avec « settings logo --file <image> »",
        [PublicBaseUrl] = "Adresse publique du site, par exemple https://formulaires.example.test (QR code, liens)",
    };

    public static bool IsKnown(string key) => Descriptions.ContainsKey(key);

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

        if (key == CseEmail && !System.Net.Mail.MailAddress.TryCreate(value, out _))
        {
            return "Adresse email invalide.";
        }

        return null;
    }
}
