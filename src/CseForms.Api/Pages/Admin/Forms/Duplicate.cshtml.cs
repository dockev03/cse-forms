using CseForms.Api.BackOffice;
using CseForms.Application.FormAdministration;
using CseForms.Application.FormDefinitions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Duplication d'un formulaire : nouvelle adresse et nouveau titre, la version en ligne de la source devient le
/// brouillon du nouveau formulaire, puis l'aperçu s'ouvre. Réservé aux administrateurs (création d'un formulaire) ;
/// les autres rôles reçoivent la page « Accès refusé », refus journalisé.
/// </summary>
public sealed class DuplicateModel(FormDuplicationService service) : PageModel
{
    public DuplicationSource Source { get; private set; } = null!;

    public IReadOnlyList<DefinitionError> Errors { get; private set; } = [];

    public string? SlugInput { get; private set; }

    public string? TitleInput { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid formId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(formId, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        SlugInput = Source.Slug.Length <= 58 ? $"{Source.Slug}-copie" : Source.Slug;
        TitleInput = Source.Title.Length <= 191 ? $"{Source.Title} (copie)" : Source.Title;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid formId, [FromForm] string? slug, [FromForm] string? title, CancellationToken cancellationToken)
    {
        switch (await service.DuplicateAsync(User.ToCurrentUser(), formId, slug, title, cancellationToken))
        {
            case DuplicationResult.Created created:
                return RedirectToPage("Preview", new { formId = created.FormId, fait = "copie" });
            case DuplicationResult.Invalid invalid:
                if (await LoadAsync(formId, cancellationToken) is { } refusal)
                {
                    return refusal;
                }

                Errors = invalid.Errors;
                SlugInput = slug;
                TitleInput = title;
                return Page();
            case DuplicationResult.Forbidden:
                return Forbid();
            default:
                return NotFound();
        }
    }

    private async Task<IActionResult?> LoadAsync(Guid formId, CancellationToken cancellationToken)
    {
        var user = User.ToCurrentUser();
        var source = await service.GetSourceAsync(user, formId, cancellationToken);
        if (source is null)
        {
            return user.IsAdmin ? NotFound() : Forbid();
        }

        Source = source;
        return null;
    }
}
