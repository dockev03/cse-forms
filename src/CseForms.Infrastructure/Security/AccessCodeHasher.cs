using CseForms.Application.Common;
using Microsoft.AspNetCore.Identity;

namespace CseForms.Infrastructure.Security;

/// <summary>Hachage PBKDF2 (hacheur d'ASP.NET Core Identity), sel aléatoire, comparaison en temps constant.</summary>
internal sealed class AccessCodeHasher : IAccessCodeHasher
{
    private static readonly object Subject = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string accessCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessCode);
        return _hasher.HashPassword(Subject, accessCode.Trim());
    }

    public bool Verify(string hash, string accessCode)
    {
        try
        {
            return _hasher.VerifyHashedPassword(Subject, hash, accessCode) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
