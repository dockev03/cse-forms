using CseForms.Api.BackOffice;
using CseForms.Application.BackOffice;
using CseForms.Application.Distribution;
using CseForms.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin;

/// <summary>
/// Double authentification de son propre compte, facultative : activation par QR code, codes de secours,
/// désactivation. Toute modification exige un code valide de l'application.
/// </summary>
public sealed class SecurityModel(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IQrCodeRenderer qrCodes,
    IAuditLog audit) : PageModel
{
    [BindProperty]
    public string Code { get; set; } = string.Empty;

    public bool Enabled { get; private set; }

    public int RecoveryCodesLeft { get; private set; }

    /// <summary>Clé à saisir à la main si le QR code ne peut pas être lu.</summary>
    public string? FormattedKey { get; private set; }

    /// <summary>QR code SVG en data URI : aucune ressource externe, compatible avec la CSP.</summary>
    public string? QrCodeDataUri { get; private set; }

    /// <summary>Codes de secours, affichés une seule fois juste après leur création.</summary>
    public IReadOnlyList<string> NewRecoveryCodes { get; private set; } = [];

    public string? Message { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await LoadAsync(await CurrentUserAsync());

    public async Task<IActionResult> OnPostEnableAsync(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync();
        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            return await LoadAsync(user);
        }

        if (!await IsValidCodeAsync(user))
        {
            ErrorMessage = "Code incorrect. Vérifiez l'heure du téléphone puis saisissez le code affiché.";
            return await LoadAsync(user);
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        NewRecoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorCodes.RecoveryCodeCount))!.ToList();
        await signInManager.RefreshSignInAsync(user);
        await audit.RecordAsync(AuditActions.TwoFactorEnabled, user.Id, null, cancellationToken);
        Message = "Double authentification activée.";
        return await LoadAsync(user);
    }

    public async Task<IActionResult> OnPostRecoveryCodesAsync(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync();
        if (!await userManager.GetTwoFactorEnabledAsync(user) || !await IsValidCodeAsync(user))
        {
            ErrorMessage = "Code incorrect.";
            return await LoadAsync(user);
        }

        NewRecoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorCodes.RecoveryCodeCount))!.ToList();
        await audit.RecordAsync(AuditActions.TwoFactorRecoveryCodesRenewed, user.Id, null, cancellationToken);
        Message = "Nouveaux codes de secours créés : les anciens ne fonctionnent plus.";
        return await LoadAsync(user);
    }

    public async Task<IActionResult> OnPostDisableAsync(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync();
        if (!await userManager.GetTwoFactorEnabledAsync(user) || !await IsValidCodeAsync(user))
        {
            ErrorMessage = "Code incorrect.";
            return await LoadAsync(user);
        }

        await userManager.SetTwoFactorEnabledAsync(user, false);
        // Nouvelle clé : une réactivation impose de scanner un nouveau QR code ; les appareils de confiance sont oubliés.
        await userManager.ResetAuthenticatorKeyAsync(user);
        await signInManager.RefreshSignInAsync(user);
        await audit.RecordAsync(AuditActions.TwoFactorDisabled, user.Id, null, cancellationToken);
        Message = "Double authentification désactivée.";
        return await LoadAsync(user);
    }

    private async Task<AppUser> CurrentUserAsync() =>
        await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("Compte connecté introuvable.");

    private async Task<bool> IsValidCodeAsync(AppUser user) =>
        TwoFactorCodes.AuthenticatorCode(Code) is { } code
        && await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, code);

    private async Task<PageResult> LoadAsync(AppUser user)
    {
        Code = string.Empty;
        Enabled = await userManager.GetTwoFactorEnabledAsync(user);
        if (Enabled)
        {
            RecoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user);
            return Page();
        }

        // Clé conservée tant que l'activation n'est pas confirmée : recharger la page ne change pas le QR code.
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            await signInManager.RefreshSignInAsync(user);
            key = (await userManager.GetAuthenticatorKeyAsync(user))!;
        }

        FormattedKey = TwoFactorCodes.FormatKey(key);
        var svg = qrCodes.Svg(TwoFactorCodes.AuthenticatorUri(user.Email!, key));
        QrCodeDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
        return Page();
    }
}
