using CseForms.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CseForms.Api.BackOffice;

/// <summary>
/// Mot de passe provisoire : tant qu'il n'est pas changé, toute page du back-office renvoie vers le changement.
/// </summary>
internal sealed class PasswordChangeFilter : IAsyncPageFilter
{
    public const string ChangePasswordPath = "/admin/mot-de-passe";

    private static readonly HashSet<string> AllowedPages = new(StringComparer.Ordinal)
    {
        "/Admin/ChangePassword",
        "/Admin/Login",
        "/Admin/LoginCode",
        "/Admin/Logout",
    };

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (context.HttpContext.User.HasClaim(c => c.Type == AppUserClaimsPrincipalFactory.MustChangePasswordClaim)
            && !AllowedPages.Contains(context.ActionDescriptor.ViewEnginePath))
        {
            context.Result = new RedirectResult(ChangePasswordPath);
            return;
        }

        await next();
    }
}
