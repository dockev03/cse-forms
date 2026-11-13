using CseForms.Api.BackOffice;
using CseForms.Application.FormAdministration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Catalogue des modèles de formulaires : modèles livrés avec l'application et modèles enregistrés par les
/// administrateurs. Réservé aux administrateurs ; les autres rôles reçoivent la page « Accès refusé ».
/// </summary>
public sealed class TemplatesModel(FormTemplateService service) : PageModel
{
    public IReadOnlyList<TemplateSummary> Templates { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = "fait")]
    public string? Done { get; set; }

    public string? Message => Done switch
    {
        "enregistre" => "Modèle enregistré : il figure ci-dessous.",
        "supprime" => "Modèle supprimé. Les formulaires créés à partir de lui ne changent pas.",
        _ => null,
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await service.ListAsync(User.ToCurrentUser(), cancellationToken) is not { } templates)
        {
            return Forbid();
        }

        Templates = templates;
        return Page();
    }
}
