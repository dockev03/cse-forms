using CseForms.Domain.Common;

namespace CseForms.Domain.Settings;

/// <summary>
/// Paramètre applicatif (bureau du CSE, SMTP, identité visuelle). Les secrets
/// (mot de passe SMTP par exemple) ne sont pas stockés ici mais en variables d'environnement.
/// </summary>
public sealed class Setting
{
    public const int KeyMaxLength = 128;

    private Setting()
    {
        Key = null!;
        Value = null!;
    }

    public Setting(string key, string value)
    {
        var normalizedKey = (key ?? string.Empty).Trim();

        if (normalizedKey.Length == 0 || normalizedKey.Length > KeyMaxLength)
        {
            throw new DomainException("Clé de paramètre invalide.");
        }

        Key = normalizedKey;
        Value = value ?? string.Empty;
    }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public void Update(string value) => Value = value ?? string.Empty;
}
