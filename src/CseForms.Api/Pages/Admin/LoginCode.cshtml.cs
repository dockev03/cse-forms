using CseForms.Api.BackOffice;
using CseForms.Api.Hosting;
using CseForms.Application.BackOffice;
using CseForms.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace CseForms.Api.Pages.Admin;

/// <summary>
/// Seconde étape de la connexion, après un mot de passe correct : code de l'application d'authentification
/// ou code de secours. Les échecs comptent dans le blocage du compte, comme ceux du mot de passe.
/// </summary>
[EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
public sealed class LoginCodeModel(SignInManager<AppUser> signInManager, IAuditLog audit) : PageModel
{
    private const string InvalidCode = "Code incorrect ou expiré.";
    private const string LockedOut = "Compte temporairement bloqué après plusieurs essais. Réessayez dans 15 minutes.";

    [BindProperty]
    public string Code { get; set; } = string.Empty;

    [BindProperty]
    public bool RememberDevice { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public int TrustedDeviceDays => (int)BackOfficeSetup.TrustedDeviceDuration.TotalDays;

    public async Task<IActionResult> OnGetAsync() =>
        await signInManager.GetTwoFactorAuthenticationUserAsync() is null ? BackToLogin() : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return BackToLogin();
        }

        SignInResult result;
        var usedRecoveryCode = false;
        if (TwoFactorCodes.AuthenticatorCode(Code) is { } code)
        {
            result = await signInManager.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: RememberDevice);
        }
        else if (TwoFactorCodes.RecoveryCode(Code) is { } recoveryCode)
        {
            // Un code de secours sert une seule fois et ne mémorise pas l'appareil.
            result = await signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
            usedRecoveryCode = true;
        }
        else
        {
            result = SignInResult.Failed;
        }

        if (result.Succeeded)
        {
            await audit.RecordAsync(AuditActions.LoginSucceeded, user.Id, null, cancellationToken);
            if (usedRecoveryCode)
            {
                await audit.RecordAsync(AuditActions.LoginRecoveryCodeUsed, user.Id, null, cancellationToken);
            }

            return LocalRedirect(LoginModel.SafeReturnUrl(Url, ReturnUrl));
        }

        if (result.IsLockedOut)
        {
            await audit.RecordAsync(AuditActions.LoginLockedOut, user.Id, null, cancellationToken);
            ErrorMessage = LockedOut;
            return Page();
        }

        await audit.RecordAsync(AuditActions.LoginTwoFactorFailed, user.Id, null, cancellationToken);
        ErrorMessage = InvalidCode;
        return Page();
    }

    private RedirectToPageResult BackToLogin() =>
        RedirectToPage("/Admin/Login", new { returnUrl = LoginModel.SafeReturnUrl(Url, ReturnUrl) });
}
