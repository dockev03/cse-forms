using Microsoft.AspNetCore.Identity;

namespace CseForms.Infrastructure.Identity;

/// <summary>Compte nominatif d'un élu. L'identifiant de connexion est l'adresse email.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
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
