using CseForms.Api.BackOffice;
using CseForms.Application.FormAdministration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Suppression définitive d'un modèle enregistré, après confirmation. Les formulaires créés à partir de lui ne
/// changent pas. Réservé aux administrateurs ; refus journalisé.
/// </summary>
public sealed class TemplateDeleteModel(FormTemplateService service) : PageModel
{
    public TemplateSummary Template { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var user = User.ToCurrentUser();
        if (await service.GetSavedAsync(user, templateId, cancellationToken) is not { } template)
        {
            return user.IsAdmin ? NotFound() : Forbid();
        }

        Template = template;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid templateId, CancellationToken cancellationToken) =>
        await service.DeleteAsync(User.ToCurrentUser(), templateId, cancellationToken) switch
        {
            LifecycleStatus.Ok => RedirectToPage("Templates", new { fait = "supprime" }),
            LifecycleStatus.Forbidden => Forbid(),
            _ => NotFound(),
        };
}
