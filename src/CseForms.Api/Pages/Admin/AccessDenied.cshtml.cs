using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin;

/// <summary>
/// Cible des refus (<c>Forbid()</c>) : rôle insuffisant pour une action sur un formulaire attribué.
/// Répond 403 sans renvoyer vers la connexion, qui redirigerait vers la page refusée (boucle).
/// Ouverte sans politique : un compte sans rôle y est aussi envoyé et ne doit pas être refusé de nouveau.
/// Aucun lien de retour construit à partir de l'adresse demandée : pas de redirection ouverte.
/// </summary>
public sealed class AccessDeniedModel : PageModel
{
    public IActionResult OnGet()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Page();
    }
}
