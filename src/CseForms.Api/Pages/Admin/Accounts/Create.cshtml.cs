using CseForms.Api.BackOffice;
using CseForms.Application.BackOffice;
using CseForms.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CseForms.Api.Pages.Admin.Accounts;

/// <summary>
/// Création d'un compte avec un mot de passe provisoire tiré au hasard, affiché une seule fois,
/// à changer par l'élu à sa première connexion.
/// </summary>
public sealed class CreateModel(AccountAdministration accounts) : AdminPageModel
{
    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string DisplayName { get; set; } = string.Empty;

    [BindProperty]
    public string Role { get; set; } = AppRoles.Editor;

    [BindProperty]
    public List<Guid> FormIds { get; set; } = [];

    public IReadOnlyList<FormChoice> Forms { get; private set; } = [];

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public Guid? CreatedId { get; private set; }

    public string? TemporaryPassword { get; private set; }

    public AccountFieldsModel Fields => new(DisplayName, Role, FormIds, Forms);

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Forms = await accounts.ListFormsAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await accounts.CreateAsync(CurrentUserId, Email, DisplayName, Role, FormIds, cancellationToken);
        Forms = await accounts.ListFormsAsync(cancellationToken);
        if (!result.Succeeded)
        {
            Errors = result.Errors;
            return Page();
        }

        // Pas de redirection : le mot de passe provisoire n'est affiché que dans cette réponse.
        CreatedId = result.UserId;
        TemporaryPassword = result.TemporaryPassword;
        return Page();
    }
}
