using CseForms.Api.BackOffice;
using CseForms.Application.BackOffice;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Submissions;

/// <summary>
/// Téléchargement du PDF déchiffré, après contrôle des droits sur le formulaire.
/// Chaque téléchargement est journalisé ; le fichier n'est jamais mis en cache.
/// </summary>
public sealed class PdfModel(BackOfficeService service) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var download = await service.DownloadPdfAsync(User.ToCurrentUser(), id, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        return File(download.Content, "application/pdf", download.FileName);
    }
}
