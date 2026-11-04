using System.Text.RegularExpressions;

namespace CseForms.Application.Settings;

/// <summary>
/// Logo du CSE (paramètre <c>cse.logo</c>), stocké en base sous forme d'adresse <c>data:</c> pour que les
/// documents n'aillent rien chercher à l'extérieur. Seuls PNG et JPEG sont acceptés : un SVG peut contenir
/// du script ou des liens.
/// </summary>
public static partial class CseLogo
{
    public const int MaxBytes = 256 * 1024;

    public const string Rule = "image PNG ou JPEG de 256 Ko au plus";

    /// <summary>Adresse <c>data:</c> du fichier ; null si ce n'est pas une image PNG ou JPEG de taille acceptée.</summary>
    public static string? ToDataUri(ReadOnlySpan<byte> content)
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

    public static bool IsValid(string? value) =>
        value is not null && value.Length <= MaxDataUriLength && DataUriPattern().IsMatch(value);

    /// <summary>Balise image du logo ; null si le paramètre est absent ou invalide.</summary>
    public static string? ToImageTag(string? value) =>
        IsValid(value) ? $"<img class=\"cse-logo\" src=\"{value}\" alt=\"Logo du CSE\">" : null;

    /// <summary>Taille du fichier d'origine, pour l'affichage (« settings list »).</summary>
    public static int DecodedSize(string value) => (value.Length - value.IndexOf(',', StringComparison.Ordinal) - 1) / 4 * 3;

    private const int MaxDataUriLength = (MaxBytes + 2) / 3 * 4 + 32;

    [GeneratedRegex(@"^data:image/(png|jpeg);base64,[A-Za-z0-9+/]+={0,2}$")]
    private static partial Regex DataUriPattern();
}
