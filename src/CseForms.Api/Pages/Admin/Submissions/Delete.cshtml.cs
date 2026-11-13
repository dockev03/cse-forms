using CseForms.Api.BackOffice;
using CseForms.Application.BackOffice;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Submissions;

/// <summary>
/// Suppression définitive d'une réponse et de son PDF, réservée aux administrateurs.
/// Il faut ressaisir le numéro de dossier pour confirmer ; la suppression est journalisée.
/// </summary>
public sealed class DeleteModel(BackOfficeService service) : PageModel
{
    public Guid SubmissionId { get; private set; }

    public Guid FormId { get; private set; }

    public string FormTitle { get; private set; } = string.Empty;

    public string CaseNumber { get; private set; } = string.Empty;

    public bool NotConfirmed { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        await ShowAsync(id, notConfirmed: false, cancellationToken);

    public async Task<IActionResult> OnPostAsync(Guid id, [FromForm] string? confirmation, CancellationToken cancellationToken)
    {
        var result = await service.DeleteSubmissionAsync(User.ToCurrentUser(), id, confirmation, cancellationToken);
        return result.Outcome switch
        {
            DeletionOutcome.Deleted => Redirect($"/admin/formulaires/{result.FormId}/reponses?supprime=1"),
            DeletionOutcome.NotConfirmed => await ShowAsync(id, notConfirmed: true, cancellationToken),
            DeletionOutcome.Forbidden => Forbid(),
            _ => NotFound(),
        };
    }

    private async Task<IActionResult> ShowAsync(Guid id, bool notConfirmed, CancellationToken cancellationToken)
    {
        var (outcome, screen) = await service.GetDeletionScreenAsync(User.ToCurrentUser(), id, cancellationToken);
        if (screen is null)
        {
            return outcome == DeletionOutcome.Forbidden ? Forbid() : NotFound();
        }

        (SubmissionId, FormId, FormTitle, CaseNumber) = screen;
        NotConfirmed = notConfirmed;
        return Page();
    }
}
