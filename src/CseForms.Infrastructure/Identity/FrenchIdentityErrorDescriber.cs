using Microsoft.AspNetCore.Identity;

namespace CseForms.Infrastructure.Identity;

/// <summary>Messages d'Identity affichés aux élus (back-office et ligne de commande), en français.</summary>
public sealed class FrenchIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"Le mot de passe doit contenir au moins {length} caractères." };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"Le mot de passe doit contenir au moins {uniqueChars} caractères différents." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "Mot de passe actuel incorrect." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = "Adresse email invalide." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = "Adresse email invalide." };

    // Doublon : même message que l'email ou l'identifiant soit en cause, sans répéter l'adresse.
    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = "Un compte existe déjà avec cette adresse email." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateEmail), Description = "Un compte existe déjà avec cette adresse email." };

    public override IdentityError InvalidToken() =>
        new() { Code = nameof(InvalidToken), Description = "Lien ou jeton expiré : recommencez." };

    public override IdentityError DefaultError() =>
        new() { Code = nameof(DefaultError), Description = "L'opération a échoué." };
}
