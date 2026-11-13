using System.Text.RegularExpressions;

namespace CseForms.Application.Settings;

/// <summary>
/// Image stockée en base sous forme d'adresse <c>data:</c> (logo, image de fond) : aucun fichier sur disque,
/// rien à aller chercher à l'extérieur, compatible avec la CSP (<c>img-src data:</c>). Seuls PNG et JPEG
/// sont acceptés, reconnus à leurs premiers octets : un SVG peut contenir du script ou des liens.
/// </summary>
public sealed partial class SettingImage(int maxBytes, string rule)
{
    public int MaxBytes { get; } = maxBytes;

    /// <summary>Règle affichée à l'utilisateur, par exemple « image PNG ou JPEG de 256 Ko au plus ».</summary>
    public string Rule { get; } = rule;

    private int MaxDataUriLength => (MaxBytes + 2) / 3 * 4 + 32;

    /// <summary>Adresse <c>data:</c> du fichier ; null si ce n'est pas une image PNG ou JPEG de taille acceptée.</summary>
    public string? ToDataUri(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0 || content.Length > MaxBytes)
        {
            return null;
        }

        var mediaType = content switch
        {
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
            [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
            _ => null,
        };

        return mediaType is null ? null : $"data:{mediaType};base64,{Convert.ToBase64String(content)}";
    }

    /// <summary>
    /// Vrai pour une adresse <c>data:</c> PNG ou JPEG en base 64 de taille acceptée. Le jeu de caractères
    /// contrôlé permet de la placer telle quelle dans un attribut HTML ou un <c>url("...")</c> CSS.
    /// </summary>
    public bool IsValid(string? value) =>
        value is not null && value.Length <= MaxDataUriLength && DataUriPattern().IsMatch(value);

    /// <summary>Contenu binaire et type d'une adresse valide, pour servir l'image telle quelle ; null sinon.</summary>
    public (byte[] Content, string MediaType)? Decode(string? value)
    {
        if (!IsValid(value))
        {
            return null;
        }

        var comma = value!.IndexOf(',', StringComparison.Ordinal);
        return (Convert.FromBase64String(value[(comma + 1)..]), value["data:".Length..value.IndexOf(';', StringComparison.Ordinal)]);
    }

    /// <summary>Taille du fichier d'origine, pour l'affichage (« settings list »).</summary>
    public static int DecodedSize(string value) => (value.Length - value.IndexOf(',', StringComparison.Ordinal) - 1) / 4 * 3;

    [GeneratedRegex(@"^data:image/(png|jpeg);base64,[A-Za-z0-9+/]+={0,2}$")]
    private static partial Regex DataUriPattern();
}
