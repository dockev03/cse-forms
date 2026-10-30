using System.Security.Claims;
using CseForms.Application.BackOffice;

namespace CseForms.Api.BackOffice;

internal static class ClaimsPrincipalExtensions
{
    public static CurrentUser ToCurrentUser(this ClaimsPrincipal principal) =>
        new(Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), principal.IsInRole(AppRoles.Admin));
}
