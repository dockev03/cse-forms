using System.Text;

namespace CseForms.Application.Settings;

/// <summary>
/// Feuille de style <c>/theme.css</c> du formulaire public, tirée des paramètres <c>theme.*</c>. Servie depuis
/// le même site, elle respecte la CSP stricte (<c>style-src 'self'</c>, sans style en ligne) ; l'image de fond
/// y figure en adresse <c>data:</c> (<c>img-src data:</c>). Seules des valeurs contrôlées y sont écrites : une
/// valeur invalide en base est ignorée.
/// </summary>
public static class ThemeStylesheet
{
    public static string Render(IReadOnlyDictionary<string, string> settings)
    {
        var css = new StringBuilder("/* Personnalisation du CSE (paramètres theme.*). */\n");

        if (settings.TryGetValue(SettingKeys.ThemePrimaryColor, out var color) && ThemeColor.IsValid(color))
        {
            css.Append(":root { --primary: ").Append(color).Append("; --primary-dark: ").Append(ThemeColor.Darken(color)).Append("; }\n");
        }

        if (settings.TryGetValue(SettingKeys.ThemeBackground, out var background) && BackgroundImage.Image.IsValid(background))
        {
            // Le contenu garde un fond uni et opaque : contrastes inchangés quelle que soit l'image.
            css.Append("body { background-image: url(\"").Append(background).Append("\"); background-size: cover; background-position: center; background-attachment: fixed; }\n")
                .Append(".page { background: var(--bg); width: calc(100% - 1.5rem); margin: 0.75rem auto; border-radius: var(--radius); }\n")
                .Append("@media (min-width: 40rem) { .page { margin: 2rem auto; box-shadow: 0 2px 12px rgba(0, 0, 0, 0.25); } }\n");
        }

        return css.ToString();
    }
}
