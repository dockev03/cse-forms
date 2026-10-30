using System.Globalization;
using System.Text.RegularExpressions;
using CseForms.Domain.Common;

namespace CseForms.Domain.Submissions;

/// <summary>Numéro de dossier lisible : <c>PRÉFIXE-AAAA-NNNN</c>, par exemple <c>INS-2026-0042</c>.</summary>
public static partial class CaseNumber
{
    public const int PrefixMaxLength = 6;

    public static string Format(string prefix, int year, long sequence)
    {
        if (year is < 2000 or > 9999 || sequence < 1)
        {
            throw new DomainException("Année ou rang de dossier invalide.");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{NormalizePrefix(prefix)}-{year:D4}-{sequence:D4}");
    }

    public static string NormalizePrefix(string prefix)
    {
        var value = (prefix ?? string.Empty).Trim().ToUpperInvariant();

        if (!PrefixPattern().IsMatch(value))
        {
            throw new DomainException($"Le préfixe de dossier doit comporter 2 à {PrefixMaxLength} lettres.");
        }

        return value;
    }

    [GeneratedRegex("^[A-Z]{2,6}$")]
    private static partial Regex PrefixPattern();
}
