using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CseForms.Application.FormDefinitions;

/// <summary>Normalisation des saisies avant validation et enregistrement.</summary>
internal static partial class TextNormalizer
{
    /// <summary>Supprime les caractères de contrôle, les espaces en tête et en fin, et réduit les espaces multiples.</summary>
    public static string SingleLine(string value) =>
        MultipleSpaces().Replace(StripControl(value, keepNewLines: false), " ").Trim();

    /// <summary>Conserve les retours à la ligne (normalisés en <c>\n</c>) et supprime les autres caractères de contrôle.</summary>
    public static string MultiLine(string value) =>
        StripControl(value.Replace("\r\n", "\n").Replace('\r', '\n'), keepNewLines: true).Trim();

    public static string Apply(TextTransform transform, string value) => transform switch
    {
        TextTransform.Upper => value.ToUpperInvariant(),
        TextTransform.Lower => value.ToLowerInvariant(),
        TextTransform.Capitalize => Capitalize(value),
        _ => value,
    };

    /// <summary>« jean-pierre d'ARC » devient « Jean-Pierre D'Arc ».</summary>
    private static string Capitalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var startOfWord = true;

        foreach (var c in value)
        {
            builder.Append(startOfWord ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            startOfWord = c is ' ' or '-' or '\'' or '’' or '\n';
        }

        return builder.ToString();
    }

    private static string StripControl(string value, bool keepNewLines)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            if (c == '\n' && keepNewLines)
            {
                builder.Append(c);
            }
            else if (c is '\t' or '\n')
            {
                builder.Append(' ');
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.Control or UnicodeCategory.Format))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex("[  ]{2,}| ")]
    private static partial Regex MultipleSpaces();
}
