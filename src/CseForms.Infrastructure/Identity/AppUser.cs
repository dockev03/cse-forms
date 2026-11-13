using Microsoft.AspNetCore.Identity;

namespace CseForms.Infrastructure.Identity;

/// <summary>Compte nominatif d'un élu. L'identifiant de connexion est l'adresse email.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Mot de passe provisoire (compte créé ou réinitialisé par un administrateur) : à changer à la connexion.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Compte désactivé par un administrateur : connexion refusée, session coupée.</summary>
    public DateTimeOffset? DisabledAt { get; set; }
}

public sealed class AppRole : IdentityRole<Guid>
{
}

/// <summary>Droit d'un compte non administrateur sur un formulaire.</summary>
public sealed class UserFormAccess
{
    public Guid UserId { get; set; }

    public Guid FormId { get; set; }
}
