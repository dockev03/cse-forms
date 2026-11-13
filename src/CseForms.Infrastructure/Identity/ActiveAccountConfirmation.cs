using Microsoft.AspNetCore.Identity;

namespace CseForms.Infrastructure.Identity;

/// <summary>
/// Vérifiée par Identity avant toute connexion (mot de passe puis code) : un compte désactivé ne peut plus se connecter.
/// </summary>
public sealed class ActiveAccountConfirmation : IUserConfirmation<AppUser>
{
    public Task<bool> IsConfirmedAsync(UserManager<AppUser> manager, AppUser user) => Task.FromResult(user.DisabledAt is null);
}
