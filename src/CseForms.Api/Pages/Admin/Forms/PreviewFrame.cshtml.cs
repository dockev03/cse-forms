using CseForms.Api.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Cadre de l'aperçu : même page et mêmes styles que le formulaire public, avec le moteur en mode aperçu
/// (<c>preview.mjs</c>), qui lit le brouillon par <c>apercu/donnees</c> et n'envoie jamais rien. La page ne contient
/// aucune donnée : les droits sur le formulaire sont contrôlés par <c>apercu/donnees</c>. Elle n'accepte d'être
/// affichée que dans une page du même site.
/// </summary>
public sealed class PreviewFrameModel : PageModel
{
    public IActionResult OnGet()
    {
        Response.Headers.ContentSecurityPolicy = SecurityHeaders.PreviewFrameContentSecurityPolicy;
        Response.Headers.XFrameOptions = "SAMEORIGIN";
        return Page();
    }
}
