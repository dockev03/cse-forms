using System.Security.Claims;
using CseForms.Application.BackOffice;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.BackOffice;

/// <summary>
/// Page réservée aux administrateurs. Les autres rôles reçoivent un 404, comme pour un formulaire non attribué :
/// l'existence de la page n'est pas révélée.
/// </summary>
public abstract class AdminPageModel : PageModel
{
    protected Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (!User.IsInRole(AppRoles.Admin))
        {
            context.Result = NotFound();
            return;
        }

        await next();
    }

    public static string RoleLabel(string role) => role switch
    {
        AppRoles.Admin => "Administrateur",
        AppRoles.Editor => "Éditeur",
        AppRoles.Reader => "Lecteur",
        _ => role,
    };
}
