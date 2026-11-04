using CseForms.Api.BackOffice;
using CseForms.Application.BackOffice;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Submissions;

/// <summary>Fiche d'une réponse. Chaque consultation est journalisée ; sans droit, la réponse est un 404.</summary>
public sealed class DetailModel(BackOfficeService service) : PageModel
{
    public SubmissionDetail Detail { get; private set; } = null!;

    public bool CanRegenerate => !User.ToCurrentUser().IsReadOnly;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await service.GetSubmissionAsync(User.ToCurrentUser(), id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;
        return Page();
    }

    /// <summary>Demande de régénération (jeton anti-CSRF vérifié), puis retour sur la fiche.</summary>
    public async Task<IActionResult> OnPostRegenerateAsync(Guid id, CancellationToken cancellationToken) =>
        await service.RegeneratePdfAsync(User.ToCurrentUser(), id, cancellationToken) switch
        {
            PdfRequestOutcome.NotFound => NotFound(),
            PdfRequestOutcome.Forbidden => Forbid(),
            _ => RedirectToPage(new { id }),
        };
}
