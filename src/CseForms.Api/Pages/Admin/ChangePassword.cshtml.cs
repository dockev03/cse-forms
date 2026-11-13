using CseForms.Application.BackOffice;
using CseForms.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin;

/// <summary>
/// Changement de son propre mot de passe. Obligatoire après un mot de passe provisoire : tant qu'il n'est pas
/// changé, les autres pages du back-office renvoient ici.
/// </summary>
public sealed class ChangePasswordModel(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, IAuditLog audit) : PageModel
{
    [BindProperty]
    public string CurrentPassword { get; set; } = string.Empty;

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    public string Confirmation { get; set; } = string.Empty;

    public bool Required { get; private set; }

    public int MinimumLength => userManager.Options.Password.RequiredLength;

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public string? Message { get; private set; }

    public async Task OnGetAsync() => Required = (await CurrentUserAsync()).MustChangePassword;

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync();
        Required = user.MustChangePassword;

        if (NewPassword != Confirmation)
        {
            Errors = ["Les deux saisies du nouveau mot de passe diffèrent."];
            return Page();
        }

        if (NewPassword == CurrentPassword)
        {
            Errors = ["Le nouveau mot de passe doit être différent de l'actuel."];
            return Page();
        }

        var changed = await userManager.ChangePasswordAsync(user, CurrentPassword, NewPassword);
        if (!changed.Succeeded)
        {
            Errors = changed.Errors.Select(e => e.Description).Distinct().ToList();
            return Page();
        }

        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);
        // Session renouvelée : l'obligation de changement disparaît, les autres sessions du compte tombent.
        await signInManager.RefreshSignInAsync(user);
        await audit.RecordAsync(AuditActions.PasswordChanged, user.Id, null, cancellationToken);

        if (Required)
        {
            return Redirect("/admin");
        }

        Message = "Mot de passe changé.";
        return Page();
    }

    private async Task<AppUser> CurrentUserAsync() =>
        await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("Compte connecté introuvable.");
}
