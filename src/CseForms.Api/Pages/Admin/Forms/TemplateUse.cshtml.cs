using CseForms.Api.BackOffice;
using CseForms.Application.FormAdministration;
using CseForms.Application.FormDefinitions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Création d'un formulaire à partir d'un modèle : adresse et titre, le modèle devient le brouillon du nouveau
/// formulaire, puis l'aperçu s'ouvre. Réservé aux administrateurs ; refus journalisé.
/// </summary>
public sealed class TemplateUseModel(FormTemplateService service) : PageModel
{
    public TemplateDetail Template { get; private set; } = null!;

    public IReadOnlyList<DefinitionError> Errors { get; private set; } = [];

    public string? SlugInput { get; private set; }

    public string? TitleInput { get; private set; }

    public async Task<IActionResult> OnGetAsync(string key, CancellationToken cancellationToken)
    {
        if (await LoadAsync(key, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        SlugInput = Template.BuiltIn ? null : Template.DefaultSlug;
        TitleInput = Template.DefaultTitle;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string key, [FromForm] string? slug, [FromForm] string? title, CancellationToken cancellationToken)
    {
        switch (await service.CreateFormAsync(User.ToCurrentUser(), key, slug, title, cancellationToken))
        {
            case TemplateUseResult.Created created:
                return RedirectToPage("Preview", new { formId = created.FormId, fait = "modele" });
            case TemplateUseResult.Invalid invalid:
                if (await LoadAsync(key, cancellationToken) is { } refusal)
                {
                    return refusal;
                }

                Errors = invalid.Errors;
                SlugInput = slug;
                TitleInput = title;
                return Page();
            case TemplateUseResult.Forbidden:
                return Forbid();
            default:
                return NotFound();
        }
    }

    private async Task<IActionResult?> LoadAsync(string key, CancellationToken cancellationToken)
    {
        var user = User.ToCurrentUser();
        var template = await service.GetAsync(user, key, cancellationToken);
        if (template is null)
        {
            return user.IsAdmin ? NotFound() : Forbid();
        }

        Template = template;
        return null;
    }
}
