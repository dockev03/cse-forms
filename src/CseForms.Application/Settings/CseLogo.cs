namespace CseForms.Application.Settings;

/// <summary>Logo du CSE (paramètre <c>cse.logo</c>), affiché sur les PDF et le flyer. Voir <see cref="SettingImage"/>.</summary>
public static class CseLogo
{
    public const int MaxBytes = 256 * 1024;

    public const string Rule = "image PNG ou JPEG de 256 Ko au plus";

    public static readonly SettingImage Image = new(MaxBytes, Rule);

    /// <summary>Adresse <c>data:</c> du fichier ; null si ce n'est pas une image PNG ou JPEG de taille acceptée.</summary>
    public static string? ToDataUri(ReadOnlySpan<byte> content) => Image.ToDataUri(content);

    public static bool IsValid(string? value) => Image.IsValid(value);

    /// <summary>Balise image du logo ; null si le paramètre est absent ou invalide.</summary>
    public static string? ToImageTag(string? value) =>
        IsValid(value) ? $"<img class=\"cse-logo\" src=\"{value}\" alt=\"Logo du CSE\">" : null;

    /// <summary>Taille du fichier d'origine, pour l'affichage (« settings list »).</summary>
    public static int DecodedSize(string value) => SettingImage.DecodedSize(value);
}
