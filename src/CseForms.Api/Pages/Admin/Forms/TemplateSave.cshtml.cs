using CseForms.Api.BackOffice;
using CseForms.Application.FormAdministration;
using CseForms.Application.FormDefinitions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Enregistrement de la version en ligne d'un formulaire comme modèle (nom, description). Réservé aux
/// administrateurs ; les autres rôles reçoivent la page « Accès refusé », refus journalisé.
/// </summary>
public sealed class TemplateSaveModel(FormTemplateService service) : PageModel
{
    public DuplicationSource Source { get; private set; } = null!;

    public IReadOnlyList<DefinitionError> Errors { get; private set; } = [];

    public string? NameInput { get; private set; }

    public string? DescriptionInput { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid formId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(formId, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        NameInput = Source.Title.Length <= 100 ? Source.Title : null;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid formId, [FromForm] string? name, [FromForm] string? description, CancellationToken cancellationToken)
    {
        switch (await service.SaveFromFormAsync(User.ToCurrentUser(), formId, name, description, cancellationToken))
        {
            case TemplateSaveResult.Saved:
                return RedirectToPage("Templates", new { fait = "enregistre" });
            case TemplateSaveResult.Invalid invalid:
                if (await LoadAsync(formId, cancellationToken) is { } refusal)
                {
                    return refusal;
                }

                Errors = invalid.Errors;
                NameInput = name;
                DescriptionInput = description;
                return Page();
            case TemplateSaveResult.Forbidden:
                return Forbid();
            default:
                return NotFound();
        }
    }

    private async Task<IActionResult?> LoadAsync(Guid formId, CancellationToken cancellationToken)
    {
        var user = User.ToCurrentUser();
        var source = await service.GetSaveSourceAsync(user, formId, cancellationToken);
        if (source is null)
        {
            return user.IsAdmin ? NotFound() : Forbid();
        }

        Source = source;
        return null;
    }
}
