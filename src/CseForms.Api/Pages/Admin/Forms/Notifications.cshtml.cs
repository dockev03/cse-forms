using CseForms.Api.BackOffice;
using CseForms.Application.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Notifications d'un formulaire : destinataires, objet et corps du message envoyé au CSE à chaque réponse,
/// avec un aperçu sur un dossier fictif. Rôles <c>admin</c> et <c>editor</c> ayant le droit sur le formulaire.
/// </summary>
public sealed class NotificationsModel(NotificationRuleService service) : PageModel
{
    public NotificationPage Notifications { get; private set; } = null!;

    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true, Name = "fait")]
    public string? Done { get; set; }

    public string RecipientsInput { get; private set; } = string.Empty;

    public string? SubjectInput { get; private set; }

    public string? BodyInput { get; private set; }

    public string? Message => Done switch
    {
        "enregistre" => "Notifications enregistrées : elles s'appliquent aux prochaines réponses.",
        "supprime" => "Notifications désactivées : plus aucun message n'est envoyé au CSE pour ce formulaire.",
        _ => null,
    };

    public static string Variables => string.Join(", ", NotificationTemplate.Variables.Select(v => $"{{{v}}}"));

    public async Task<IActionResult> OnGetAsync(Guid formId, CancellationToken cancellationToken) =>
        await ShowAsync(formId, error: null, cancellationToken);

    public async Task<IActionResult> OnPostSaveAsync(
        Guid formId,
        [FromForm] string? recipients,
        [FromForm] string? subject,
        [FromForm] string? body,
        CancellationToken cancellationToken)
    {
        var result = await service.SaveAsync(User.ToCurrentUser(), formId, recipients, subject, body, cancellationToken);
        if (result.Status == NotificationStatus.Invalid)
        {
            RecipientsInput = recipients ?? string.Empty;
            SubjectInput = subject;
            BodyInput = body;
            return await ShowAsync(formId, result.Error, cancellationToken, keepInputs: true);
        }

        return Respond(formId, result, "enregistre");
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid formId, CancellationToken cancellationToken) =>
        Respond(formId, await service.RemoveAsync(User.ToCurrentUser(), formId, cancellationToken), "supprime");

    private IActionResult Respond(Guid formId, NotificationResult result, string done) =>
        result.Status switch
        {
            NotificationStatus.Ok => RedirectToPage(new { formId, fait = done }),
            NotificationStatus.Forbidden => Forbid(),
            _ => NotFound(),
        };

    private async Task<IActionResult> ShowAsync(Guid formId, string? error, CancellationToken cancellationToken, bool keepInputs = false)
    {
        var result = await service.GetPageAsync(User.ToCurrentUser(), formId, cancellationToken);
        switch (result.Status)
        {
            case NotificationStatus.Ok:
                Notifications = result.Value!;
                Error = error;
                if (error is not null)
                {
                    Done = null;
                }

                if (!keepInputs && Notifications.Rule is { } rule)
                {
                    RecipientsInput = string.Join('\n', rule.Recipients);
                    SubjectInput = rule.Subject;
                    BodyInput = rule.Body;
                }

                return Page();
            case NotificationStatus.Forbidden:
                return Forbid();
            default:
                return NotFound();
        }
    }
}
