using CseForms.Api.BackOffice;
using CseForms.Application.BackOffice;
using CseForms.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin;

/// <summary>Déconnexion uniquement en POST, avec jeton anti-CSRF.</summary>
public sealed class LogoutModel(SignInManager<AppUser> signInManager, IAuditLog audit) : PageModel
{
    public IActionResult OnGet() => Redirect("/admin");

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var userId = User.ToCurrentUser().Id;
        await signInManager.SignOutAsync();
        await audit.RecordAsync(AuditActions.Logout, userId, null, cancellationToken);
        return Redirect(BackOfficeSetup.LoginPath);
    }
}
