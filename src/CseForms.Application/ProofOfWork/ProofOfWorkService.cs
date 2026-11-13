using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CseForms.Application.ProofOfWork;

/// <summary>Réglages de la preuve de travail (section <c>ProofOfWork</c>). La clé vient de l'environnement, jamais du dépôt.</summary>
public sealed class ProofOfWorkOptions
{
    public const string Section = "ProofOfWork";

    /// <summary>Clé HMAC-SHA256 en base64 (32 octets) : <c>ProofOfWork__HmacKey</c>.</summary>
    public string HmacKey { get; set; } = string.Empty;

    /// <summary>Borne haute du nombre à trouver : en moyenne, le navigateur calcule la moitié de ces empreintes.</summary>
    public int MaxNumber { get; set; } = 50_000;

    /// <summary>Durée de validité d'un défi, assez longue pour remplir le formulaire.</summary>
    public int ExpiresMinutes { get; set; } = 20;

    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var buffer = new byte[64];
        return Convert.TryFromBase64String(key, buffer, out var length) && length == 32;
    }
}

/// <summary>Défi envoyé au navigateur, au format ALTCHA.</summary>
public sealed record ProofOfWorkChallenge(string Algorithm, string Challenge, int MaxNumber, string Salt, string Signature);

/// <summary>Solution renvoyée avec la réponse au formulaire, au format ALTCHA.</summary>
public sealed record ProofOfWorkSolution(string? Algorithm, string? Challenge, long Number, string? Salt, string? Signature);

public interface IProofOfWorkStore
{
    /// <summary>Grille le défi ; renvoie faux s'il a déjà servi. Opération atomique.</summary>
    Task<bool> TryConsumeAsync(string challenge, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Supprime les défis expirés, qui ne peuvent plus être rejoués de toute façon.</summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// Preuve de travail auto-hébergée, compatible avec le protocole ALTCHA : le navigateur cherche
/// <c>n</c> tel que SHA-256(sel + n) = défi. Sans service tiers ni traceur ; le défi est signé (HMAC),
/// limité dans le temps et à usage unique. Il complète le code d'accès et la limitation de débit.
/// </summary>
public sealed class ProofOfWorkService(ProofOfWorkOptions options, IProofOfWorkStore store, TimeProvider timeProvider)
{
    public const string Algorithm = "SHA-256";

    private const string ExpiresParameter = "?expires=";
    private const int MaxSaltLength = 100;

    private readonly byte[] _key = Convert.FromBase64String(options.HmacKey);

    public ProofOfWorkChallenge CreateChallenge()
    {
        var expires = timeProvider.GetUtcNow().AddMinutes(options.ExpiresMinutes).ToUnixTimeSeconds();
        var salt = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12)) + ExpiresParameter + expires.ToString(CultureInfo.InvariantCulture);
        var number = RandomNumberGenerator.GetInt32(0, options.MaxNumber + 1);
        var challenge = Hash(salt, number);

        return new ProofOfWorkChallenge(Algorithm, challenge, options.MaxNumber, salt, Sign(challenge));
    }

    /// <summary>
    /// Vérifie la solution et la grille : signature, expiration, empreinte, puis usage unique.
    /// Une solution vérifiée ne resservira pas, même si la réponse est ensuite refusée.
    /// </summary>
    public async Task<bool> VerifyAsync(ProofOfWorkSolution? solution, CancellationToken cancellationToken)
    {
        if (solution is not { Algorithm: Algorithm, Challenge.Length: 64, Salt: { Length: <= MaxSaltLength } salt, Signature: { } signature }
            || solution.Number < 0
            || solution.Number > options.MaxNumber)
        {
            return false;
        }

        if (!FixedTimeEquals(Sign(solution.Challenge), signature)
            || ExpiresAt(salt) is not { } expiresAt
            || expiresAt <= timeProvider.GetUtcNow()
            || !FixedTimeEquals(Hash(salt, solution.Number), solution.Challenge))
        {
            return false;
        }

        return await store.TryConsumeAsync(solution.Challenge.ToLowerInvariant(), expiresAt, cancellationToken);
    }

    public static string Hash(string salt, long number) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(salt + number.ToString(CultureInfo.InvariantCulture))));

    private string Sign(string challenge) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(challenge.ToLowerInvariant())));

    private static DateTimeOffset? ExpiresAt(string salt)
    {
        var index = salt.IndexOf(ExpiresParameter, StringComparison.Ordinal);
        return index >= 0
            && long.TryParse(salt.AsSpan(index + ExpiresParameter.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds is > 0 and < 253402300799
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
    }

    private static bool FixedTimeEquals(string expected, string actual) =>
        expected.Length == actual.Length
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual.ToLowerInvariant()));
}
