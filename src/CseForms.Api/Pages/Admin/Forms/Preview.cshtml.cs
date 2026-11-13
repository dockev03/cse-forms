using CseForms.Api.BackOffice;
using CseForms.Api.Hosting;
using CseForms.Application.FormAdministration;
using CseForms.Application.FormDefinitions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Aperçu du brouillon d'un formulaire, en largeur mobile ou ordinateur, puis publication ou suppression.
/// La page reste sans script : le rendu se fait dans un cadre du même site (<c>apercu/cadre</c>), qui exécute
/// le moteur du formulaire public avec l'envoi désactivé. Rôles <c>admin</c> et <c>editor</c> ayant le droit
/// sur le formulaire ; publication et suppression en POST avec jeton anti-CSRF, journalisées.
/// </summary>
public sealed class PreviewModel(FormDraftService service) : PageModel
{
    public const string MobileView = "mobile";
    public const string DesktopView = "ordinateur";

    /// <summary>Null : le formulaire n'a pas de brouillon.</summary>
    public DraftPreview? Draft { get; private set; }

    public Guid FormId { get; private set; }

    public IReadOnlyList<DefinitionError> Errors { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = "vue")]
    public string? View { get; set; }

    [BindProperty(SupportsGet = true, Name = "fait")]
    public string? Done { get; set; }

    public bool IsDesktop => View == DesktopView;

    public string? Message => Done switch
    {
        "brouillon" => "Brouillon enregistré, rien n'a été publié. Vérifiez le rendu ci-dessous, puis publiez-le.",
        "copie" => "Formulaire dupliqué, hors ligne : la copie est en brouillon. Vérifiez le rendu ci-dessous, puis publiez-la.",
        "modele" => "Formulaire créé à partir du modèle, hors ligne : il est en brouillon. Vérifiez le rendu ci-dessous, puis publiez-le.",
        "editeur" => "Brouillon enregistré depuis l'éditeur, rien n'a été publié. Vérifiez le rendu ci-dessous, puis publiez-le.",
        _ => null,
    };

    public async Task<IActionResult> OnGetAsync(Guid formId, CancellationToken cancellationToken) =>
        await ShowAsync(formId, cancellationToken);

    /// <summary>Le code saisi n'est jamais renvoyé à la page, même en cas d'erreur.</summary>
    public async Task<IActionResult> OnPostPublishAsync(
        Guid formId,
        [FromForm] string? code,
        [FromForm] string? confirmation,
        [FromForm] bool confirmPublic,
        CancellationToken cancellationToken)
    {
        var result = await service.PublishAsync(User.ToCurrentUser(), formId, code, confirmation, confirmPublic, cancellationToken);
        switch (result.Status)
        {
            case LifecycleStatus.Forbidden:
                return Forbid();
            case LifecycleStatus.NotFound:
                return NotFound();
        }

        switch (result.Import)
        {
            case FormImportResult.Done { Unchanged: true }:
                return RedirectToPage("Settings", new { formId, fait = "brouillon-identique" });
            case FormImportResult.Done done:
                return RedirectToPage("Settings", new { formId, fait = done.StillClosed ? "brouillon-publie-ferme" : "brouillon-publie" });
            case FormImportResult.Invalid invalid:
                Errors = invalid.Errors;
                break;
            case FormImportResult.AccessCodeRequired:
                Errors = [new DefinitionError("code", "Ce formulaire est protégé par un code d'accès, pas encore défini : saisissez-le deux fois, puis publiez.")];
                break;
            case FormImportResult.ConfirmPublicRequired:
                Errors = [new DefinitionError("$.access", "Ce brouillon rend public un formulaire protégé par un code d'accès : cochez la confirmation, puis publiez.")];
                break;
            default:
                return Forbid();
        }

        Done = null;
        return await ShowAsync(formId, cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid formId, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(User.ToCurrentUser(), formId, cancellationToken)).Status switch
        {
            LifecycleStatus.Ok => RedirectToPage("Settings", new { formId, fait = "brouillon-supprime" }),
            LifecycleStatus.Forbidden => Forbid(),
            _ => NotFound(),
        };

    public string ViewUrl(string view) => $"/admin/formulaires/{FormId}/apercu?vue={view}";

    private async Task<IActionResult> ShowAsync(Guid formId, CancellationToken cancellationToken)
    {
        var result = await service.GetPreviewAsync(User.ToCurrentUser(), formId, cancellationToken);
        switch (result.Status)
        {
            case LifecycleStatus.Ok:
                FormId = formId;
                Draft = result.Value;
                if (Draft is not null)
                {
                    Errors = [.. Draft.Errors, .. Errors];
                }

                View = View == DesktopView ? DesktopView : MobileView;

                // Seule page du back-office qui affiche un cadre, et seulement du même site.
                Response.Headers.ContentSecurityPolicy = SecurityHeaders.PreviewHostContentSecurityPolicy;
                return Page();
            case LifecycleStatus.Forbidden:
                return Forbid();
            default:
                return NotFound();
        }
    }
}
