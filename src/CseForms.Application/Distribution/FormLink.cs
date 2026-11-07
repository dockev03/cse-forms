using System.Globalization;
using System.Text;
using CseForms.Application.FormDefinitions;

namespace CseForms.Application.Distribution;

/// <summary>Lien public d'un formulaire, éventuellement prérempli avec un site (un QR code par site).</summary>
public static class FormLink
{
    public static string Build(string baseUrl, string slug, string? siteParameter = null, string? site = null)
    {
        var url = $"{baseUrl.TrimEnd('/')}/f/{Uri.EscapeDataString(slug)}";
        return siteParameter is null || site is null
            ? url
            : $"{url}?{Uri.EscapeDataString(siteParameter)}={Uri.EscapeDataString(site)}";
    }

    /// <summary>Champ « site » préremplissable par l'URL ; null si le formulaire n'en a pas.</summary>
    public static string? SiteParameter(string? schemaJson)
    {
        if (schemaJson is null)
        {
            return null;
        }

        var parsed = FormDefinitionParser.Parse(schemaJson);
        return parsed.IsValid
            ? parsed.Definition!.AllTopLevelFields.FirstOrDefault(f => f.Type == FieldType.Site && f.Prefill == "url")?.Id
            : null;
    }

    /// <summary>Partie de nom de fichier sans accent ni caractère spécial : « Saint-Étienne » donne « saint-etienne ».</summary>
    public static string FileNamePart(string text)
    {
        var result = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                result.Append(char.ToLowerInvariant(c));
            }
            else if (result.Length > 0 && result[^1] != '-')
            {
                result.Append('-');
            }
        }

        return result.ToString().Trim('-') is { Length: > 0 } name ? name : "site";
    }
}
