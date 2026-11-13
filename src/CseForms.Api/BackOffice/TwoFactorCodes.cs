using System.Text;

namespace CseForms.Api.BackOffice;

/// <summary>
/// Double authentification par application (TOTP, RFC 6238) : saisie des codes et lien de configuration.
/// Les codes eux-mêmes sont générés et vérifiés par ASP.NET Identity.
/// </summary>
internal static class TwoFactorCodes
{
    public const int RecoveryCodeCount = 10;

    /// <summary>Nom affiché dans l'application d'authentification de l'élu.</summary>
    public const string Issuer = "Back-office CSE";

    /// <summary>Code à 6 chiffres de l'application, espaces et tirets tolérés ; null sinon.</summary>
    public static string? AuthenticatorCode(string? input)
    {
        var code = Compact(input);
        return code.Length == 6 && code.All(char.IsAsciiDigit) ? code : null;
    }

    /// <summary>
    /// Code de secours au format d'Identity (<c>XXXXX-XXXXX</c>), saisi avec ou sans tiret, en minuscules ou majuscules ; null sinon.
    /// </summary>
    public static string? RecoveryCode(string? input)
    {
        var code = Compact(input).ToUpperInvariant();
        return code.Length == 10 && code.All(char.IsAsciiLetterOrDigit) ? $"{code[..5]}-{code[5..]}" : null;
    }

    /// <summary>Clé affichée par groupes de 4 caractères pour une saisie manuelle.</summary>
    public static string FormatKey(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return builder.ToString().ToLowerInvariant();
    }

    /// <summary>Lien <c>otpauth://</c> lu par les applications d'authentification (QR code).</summary>
    public static string AuthenticatorUri(string account, string key) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";

    private static string Compact(string? input) =>
        new((input ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
}
