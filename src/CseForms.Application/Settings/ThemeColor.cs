using System.Globalization;
using System.Text.RegularExpressions;

namespace CseForms.Application.Settings;

/// <summary>
/// Couleur principale du formulaire public (paramètre <c>theme.primaryColor</c>), au format <c>#rrggbb</c>.
/// Elle sert de fond aux boutons (texte blanc) et de couleur de texte sur fond clair : elle est refusée si
/// son contraste avec ces fonds est inférieur à 4,5:1 (RGAA, critère 3.2, texte de taille normale).
/// </summary>
public static partial class ThemeColor
{
    public const string Default = "#1d4f91";

    public const double MinContrast = 4.5;

    /// <summary>Fonds clairs sur lesquels la couleur sert de texte (cartes, fond de page) ; le blanc est aussi le texte des boutons.</summary>
    public static readonly IReadOnlyList<string> LightBackgrounds = ["#ffffff", "#f5f6f8"];

    /// <summary><c>#rgb</c> ou <c>#rrggbb</c>, majuscules acceptées, ramené à <c>#rrggbb</c> en minuscules ; sinon la valeur telle quelle.</summary>
    public static string Normalize(string value)
    {
        var match = ShortPattern().Match(value);
        return match.Success
            ? $"#{match.Groups[1].Value}{match.Groups[1].Value}{match.Groups[2].Value}{match.Groups[2].Value}{match.Groups[3].Value}{match.Groups[3].Value}".ToLowerInvariant()
            : value.ToLowerInvariant();
    }

    public static bool IsWellFormed(string? value) => value is not null && Pattern().IsMatch(value);

    /// <summary>Couleur bien formée et assez contrastée : seule une telle valeur est écrite dans la feuille de style.</summary>
    public static bool IsValid(string? value) => Validate(value ?? string.Empty) is null;

    /// <summary>Contrôle d'une valeur normalisée ; null si elle est acceptée.</summary>
    public static string? Validate(string value)
    {
        if (!IsWellFormed(value))
        {
            return "Couleur attendue au format #rrggbb, par exemple #1d4f91.";
        }

        var worst = LightBackgrounds.Min(background => ContrastRatio(value, background));
        return worst < MinContrast
            ? string.Create(CultureInfo.GetCultureInfo("fr-FR"), $"Contraste insuffisant ({Math.Floor(worst * 10) / 10:0.0}:1 avec le texte blanc et les fonds clairs, {MinContrast:0.0}:1 au minimum, RGAA). Choisissez une couleur plus foncée.")
            : null;
    }

    /// <summary>Rapport de contraste WCAG 2 entre deux couleurs <c>#rrggbb</c>, de 1 à 21.</summary>
    public static double ContrastRatio(string first, string second)
    {
        var (a, b) = (RelativeLuminance(first), RelativeLuminance(second));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>Variante plus sombre (survol des boutons) : chaque composante réduite de 20 %, contraste avec le blanc encore meilleur.</summary>
    public static string Darken(string value)
    {
        var (r, g, b) = Channels(value);
        return $"#{(int)(r * 0.8):x2}{(int)(g * 0.8):x2}{(int)(b * 0.8):x2}";
    }

    private static double RelativeLuminance(string value)
    {
        var (r, g, b) = Channels(value);
        return (0.2126 * Linear(r)) + (0.7152 * Linear(g)) + (0.0722 * Linear(b));

        static double Linear(int channel)
        {
            var c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }

    private static (int R, int G, int B) Channels(string value) =>
        (Hex(value, 1), Hex(value, 3), Hex(value, 5));

    private static int Hex(string value, int start) => int.Parse(value.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    [GeneratedRegex("^#[0-9a-f]{6}$")]
    private static partial Regex Pattern();

    [GeneratedRegex("^#([0-9a-fA-F])([0-9a-fA-F])([0-9a-fA-F])$")]
    private static partial Regex ShortPattern();
}
