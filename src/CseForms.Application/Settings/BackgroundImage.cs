namespace CseForms.Application.Settings;

/// <summary>
/// Image de fond du formulaire public (paramètre <c>theme.background</c>), servie dans la feuille
/// <c>/theme.css</c>. Décorative : le formulaire reste sur un fond uni, la lisibilité ne dépend pas d'elle.
/// Voir <see cref="SettingImage"/>.
/// </summary>
public static class BackgroundImage
{
    public const int MaxBytes = 512 * 1024;

    public const string Rule = "image PNG ou JPEG de 512 Ko au plus";

    public static readonly SettingImage Image = new(MaxBytes, Rule);
}
