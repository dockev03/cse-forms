using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CseForms.Infrastructure.Identity;

/// <summary>
/// Ajoute à la session l'obligation de changer un mot de passe provisoire : le back-office n'affiche alors
/// que la page de changement. Recalculé à chaque connexion et à chaque contrôle du jeton de sécurité.
/// </summary>
public sealed class AppUserClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, AppRole>(userManager, roleManager, options)
{
    public const string MustChangePasswordClaim = "cse:must_change_password";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(MustChangePasswordClaim, "true"));
        }

        return identity;
    }
}
