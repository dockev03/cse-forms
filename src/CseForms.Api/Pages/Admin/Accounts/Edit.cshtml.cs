using CseForms.Api.BackOffice;
using CseForms.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CseForms.Api.Pages.Admin.Accounts;

/// <summary>
/// Modification d'un compte : nom, rôle, formulaires, désactivation, mot de passe provisoire, double authentification.
/// </summary>
public sealed class EditModel(AccountAdministration accounts) : AdminPageModel
{
    [BindProperty]
    public string DisplayName { get; set; } = string.Empty;

    [BindProperty]
    public string Role { get; set; } = string.Empty;

    [BindProperty]
    public List<Guid> FormIds { get; set; } = [];

    public AccountDetail Account { get; private set; } = null!;

    public IReadOnlyList<FormChoice> Forms { get; private set; } = [];

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public string? Message { get; private set; }

    public string? TemporaryPassword { get; private set; }

    public bool IsSelf => Account.Id == CurrentUserId;

    public AccountFieldsModel Fields => new(DisplayName, Role, FormIds, Forms);

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        await ShowAsync(id, reloadFields: true, cancellationToken);

    public async Task<IActionResult> OnPostSaveAsync(Guid id, CancellationToken cancellationToken) =>
        await ApplyAsync(id, accounts.UpdateAsync(CurrentUserId, id, DisplayName, Role, FormIds, cancellationToken), "Compte enregistré.", cancellationToken);

    public async Task<IActionResult> OnPostDisableAsync(Guid id, CancellationToken cancellationToken) =>
        await ApplyAsync(id, accounts.SetDisabledAsync(CurrentUserId, id, disabled: true, cancellationToken), "Compte désactivé : sa session sera coupée sous 5 minutes.", cancellationToken);

    public async Task<IActionResult> OnPostEnableAsync(Guid id, CancellationToken cancellationToken) =>
        await ApplyAsync(id, accounts.SetDisabledAsync(CurrentUserId, id, disabled: false, cancellationToken), "Compte réactivé.", cancellationToken);

    public async Task<IActionResult> OnPostResetPasswordAsync(Guid id, CancellationToken cancellationToken) =>
        await ApplyAsync(id, accounts.ResetPasswordAsync(CurrentUserId, id, cancellationToken), "Mot de passe réinitialisé : les sessions ouvertes du compte seront coupées sous 5 minutes.", cancellationToken);

    public async Task<IActionResult> OnPostResetTwoFactorAsync(Guid id, CancellationToken cancellationToken) =>
        await ApplyAsync(id, accounts.ResetTwoFactorAsync(CurrentUserId, id, cancellationToken), "Double authentification désactivée : connexion par mot de passe seul jusqu'à sa réactivation par l'élu.", cancellationToken);

    private async Task<IActionResult> ApplyAsync(Guid id, Task<AccountResult> action, string message, CancellationToken cancellationToken)
    {
        var result = await action;
        if (result.Succeeded)
        {
            Message = message;
            TemporaryPassword = result.TemporaryPassword;
        }
        else
        {
            Errors = result.Errors;
        }

        // Après une erreur d'enregistrement, on garde la saisie ; sinon on recharge l'état du compte.
        var keepInput = !result.Succeeded && Request.Query["handler"] == "Save";
        return await ShowAsync(id, reloadFields: !keepInput, cancellationToken);
    }

    private async Task<IActionResult> ShowAsync(Guid id, bool reloadFields, CancellationToken cancellationToken)
    {
        if (await accounts.GetAsync(id, cancellationToken) is not { } account)
        {
            return NotFound();
        }

        Account = account;
        Forms = await accounts.ListFormsAsync(cancellationToken);
        if (reloadFields)
        {
            DisplayName = account.DisplayName;
            Role = account.Role;
            FormIds = [.. account.FormIds];
        }

        return Page();
    }
}
