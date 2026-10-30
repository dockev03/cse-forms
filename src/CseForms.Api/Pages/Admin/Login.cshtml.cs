using CseForms.Api.Hosting;
using CseForms.Application.BackOffice;
using CseForms.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CseForms.Api.Pages.Admin;

[EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
public sealed class LoginModel(SignInManager<AppUser> signInManager, UserManager<AppUser> userManager, IAuditLog audit) : PageModel
{
    // Même message dans tous les cas d'échec : ne révèle pas si le compte existe.
    private const string InvalidCredentials = "Identifiant ou mot de passe incorrect.";
    private const string LockedOut = "Compte temporairement bloqué après plusieurs essais. Réessayez dans 15 minutes.";

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? LocalRedirect(SafeReturnUrl()) : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = string.IsNullOrWhiteSpace(Email) ? null : await userManager.FindByEmailAsync(Email.Trim());
        if (user is null || string.IsNullOrEmpty(Password))
        {
            await audit.RecordAsync(AuditActions.LoginFailed, null, null, cancellationToken);
            ErrorMessage = InvalidCredentials;
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(user, Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            await audit.RecordAsync(AuditActions.LoginSucceeded, user.Id, null, cancellationToken);
            return LocalRedirect(SafeReturnUrl());
        }

        if (result.IsLockedOut)
        {
            await audit.RecordAsync(AuditActions.LoginLockedOut, user.Id, null, cancellationToken);
            ErrorMessage = LockedOut;
            return Page();
        }

        await audit.RecordAsync(AuditActions.LoginFailed, user.Id, null, cancellationToken);
        ErrorMessage = InvalidCredentials;
        return Page();
    }

    /// <summary>Seule une adresse du back-office est acceptée : pas de redirection ouverte.</summary>
    private string SafeReturnUrl() =>
        ReturnUrl is { } url && Url.IsLocalUrl(url) && url.StartsWith("/admin", StringComparison.Ordinal) ? url : "/admin";
}
