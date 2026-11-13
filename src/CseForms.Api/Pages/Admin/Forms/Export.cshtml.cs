using CseForms.Api.BackOffice;
using CseForms.Application.BackOffice;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Export CSV des réponses filtrées (mêmes filtres que la liste, toutes les pages). Réservé aux
/// administrateurs et éditeurs ; chaque export est journalisé ; le fichier n'est jamais mis en cache.
/// </summary>
public sealed class ExportModel(BackOfficeService service) : PageModel
{
    public async Task<IActionResult> OnGetAsync(
        Guid formId,
        [FromQuery] string? q,
        [FromQuery] string? statut,
        [FromQuery] string? site,
        [FromQuery] DateOnly? du,
        [FromQuery] DateOnly? au,
        [FromQuery] string? tri,
        CancellationToken cancellationToken)
    {
        var filter = new SubmissionFilter(q, statut, site, du, au, tri == "ancien");
        var result = await service.ExportCsvAsync(User.ToCurrentUser(), formId, filter, cancellationToken);

        return result.Outcome switch
        {
            ExportOutcome.Ok => File(result.File!.Content, "text/csv; charset=utf-8", result.File.FileName),
            ExportOutcome.Forbidden => Forbid(),
            _ => NotFound(),
        };
    }
}
