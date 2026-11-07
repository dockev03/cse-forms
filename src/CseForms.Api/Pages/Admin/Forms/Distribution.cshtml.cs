using CseForms.Api.BackOffice;
using CseForms.Application.Distribution;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Diffusion d'un formulaire : lien public, QR code (PNG, SVG) global ou par site, flyer PDF.
/// Réservée aux administrateurs et éditeurs ; chaque QR code et flyer produit est journalisé.
/// </summary>
public sealed class DistributionModel(DistributionService service) : PageModel
{
    public DistributionPage Distribution { get; private set; } = null!;

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid formId, CancellationToken cancellationToken) =>
        await ShowAsync(formId, error: null, cancellationToken);

    public async Task<IActionResult> OnGetQrAsync(Guid formId, string? site, string? format, CancellationToken cancellationToken)
    {
        var qrFormat = format == "svg" ? QrFormat.Svg : QrFormat.Png;
        var result = await service.QrCodeAsync(User.ToCurrentUser(), formId, site, qrFormat, cancellationToken);
        return await RespondAsync(formId, result, cancellationToken);
    }

    /// <summary>Flyer produit à la demande (jeton anti-CSRF vérifié) : le code saisi n'est ni conservé ni renvoyé à la page.</summary>
    public async Task<IActionResult> OnPostFlyerAsync(Guid formId, [FromForm] string? site, [FromForm] string? code, CancellationToken cancellationToken)
    {
        var result = await service.FlyerAsync(User.ToCurrentUser(), formId, site, code, cancellationToken);
        return await RespondAsync(formId, result, cancellationToken);
    }

    private async Task<IActionResult> RespondAsync(Guid formId, DistributionResult<DistributionFile> result, CancellationToken cancellationToken) =>
        result.Status switch
        {
            DistributionStatus.Ok => File(result.Value!.Content, result.Value.ContentType, result.Value.FileName),
            DistributionStatus.Invalid => await ShowAsync(formId, result.Error, cancellationToken),
            DistributionStatus.Forbidden => Forbid(),
            _ => NotFound(),
        };

    private async Task<IActionResult> ShowAsync(Guid formId, string? error, CancellationToken cancellationToken)
    {
        var result = await service.GetPageAsync(User.ToCurrentUser(), formId, cancellationToken);
        switch (result.Status)
        {
            case DistributionStatus.Ok:
                Distribution = result.Value!;
                Error = error;
                return Page();
            case DistributionStatus.Forbidden:
                return Forbid();
            default:
                return NotFound();
        }
    }
}
