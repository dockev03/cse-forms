using System.Globalization;
using CseForms.Api.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.FormAdministration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Réglages d'un formulaire : fermeture et réouverture, dates d'ouverture et de fermeture, nombre maximal
/// de réponses, code d'accès, filigrane du PDF. Rôles <c>admin</c> et <c>editor</c> ayant le droit sur le formulaire ;
/// chaque action est un POST avec jeton anti-CSRF, journalisé.
/// </summary>
public sealed class SettingsModel(FormLifecycleService service) : PageModel
{
    /// <summary>Format d'un champ <c>datetime-local</c>, sans les secondes.</summary>
    public const string LocalInputFormat = "yyyy-MM-dd'T'HH:mm";

    public FormLifecyclePage Form { get; private set; } = null!;

    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true, Name = "fait")]
    public string? Done { get; set; }

    /// <summary>Valeurs saisies, réaffichées telles quelles en cas d'erreur.</summary>
    public string? OpensAtInput { get; private set; }

    public string? ClosesAtInput { get; private set; }

    public string? MaxResponsesInput { get; private set; }

    public string? Message => Done switch
    {
        "ferme" => "Formulaire fermé : il n'accepte plus de réponse.",
        "rouvert" => "Formulaire rouvert.",
        "calendrier" => "Dates et nombre maximal de réponses enregistrés.",
        "code" => "Code d'accès changé. Les flyers déjà imprimés portent l'ancien code : pensez à les remplacer.",
        "filigrane" => "Filigrane activé : les PDF téléchargés porteront le nom de la personne et la date.",
        "sans-filigrane" => "Filigrane désactivé.",
        "brouillon-publie" => "Brouillon publié : la nouvelle version est en ligne. Les réponses déjà reçues restent rattachées à leur version.",
        "brouillon-publie-ferme" => "Brouillon publié. Le formulaire reste fermé : rouvrez-le ci-dessous pour le mettre en ligne.",
        "brouillon-identique" => "Le brouillon était identique à la version en ligne : rien n'a été publié, le brouillon est supprimé.",
        "brouillon-supprime" => "Brouillon supprimé. La version en ligne ne change pas.",
        _ => null,
    };

    public async Task<IActionResult> OnGetAsync(Guid formId, CancellationToken cancellationToken) =>
        await ShowAsync(formId, error: null, cancellationToken);

    public async Task<IActionResult> OnPostCloseAsync(Guid formId, CancellationToken cancellationToken) =>
        await RespondAsync(formId, await service.CloseAsync(User.ToCurrentUser(), formId, cancellationToken), "ferme", cancellationToken);

    public async Task<IActionResult> OnPostReopenAsync(Guid formId, CancellationToken cancellationToken) =>
        await RespondAsync(formId, await service.ReopenAsync(User.ToCurrentUser(), formId, cancellationToken), "rouvert", cancellationToken);

    public async Task<IActionResult> OnPostScheduleAsync(
        Guid formId,
        [FromForm] string? opensAt,
        [FromForm] string? closesAt,
        [FromForm] string? maxResponses,
        CancellationToken cancellationToken)
    {
        OpensAtInput = opensAt;
        ClosesAtInput = closesAt;
        MaxResponsesInput = maxResponses;

        if (!TryParseLocal(opensAt, out var opens))
        {
            return await ShowAsync(formId, "La date d'ouverture est invalide.", cancellationToken, keepInputs: true);
        }

        if (!TryParseLocal(closesAt, out var closes))
        {
            return await ShowAsync(formId, "La date de fermeture est invalide.", cancellationToken, keepInputs: true);
        }

        if (!TryParseCount(maxResponses, out var max))
        {
            return await ShowAsync(formId, "Le nombre maximal de réponses doit être un nombre entier positif.", cancellationToken, keepInputs: true);
        }

        var result = await service.UpdateScheduleAsync(User.ToCurrentUser(), formId, opens, closes, max, cancellationToken);
        return await RespondAsync(formId, result, "calendrier", cancellationToken, keepInputs: true);
    }

    /// <summary>Le code saisi n'est jamais renvoyé à la page, même en cas d'erreur.</summary>
    public async Task<IActionResult> OnPostAccessCodeAsync(
        Guid formId,
        [FromForm] string? code,
        [FromForm] string? confirmation,
        CancellationToken cancellationToken) =>
        await RespondAsync(
            formId,
            await service.ChangeAccessCodeAsync(User.ToCurrentUser(), formId, code, confirmation, cancellationToken),
            "code",
            cancellationToken);

    public async Task<IActionResult> OnPostWatermarkAsync(Guid formId, [FromForm] bool enabled, CancellationToken cancellationToken) =>
        await RespondAsync(
            formId,
            await service.SetPdfWatermarkAsync(User.ToCurrentUser(), formId, enabled, cancellationToken),
            enabled ? "filigrane" : "sans-filigrane",
            cancellationToken);

    public static string LocalInput(DateTimeOffset? utc) =>
        utc is { } value ? CseCalendar.ToLocal(value).ToString(LocalInputFormat, CultureInfo.InvariantCulture) : string.Empty;

    public static string StatusLabel(FormAvailability availability) => availability switch
    {
        FormAvailability.Open => "Ouvert : accepte les réponses",
        FormAvailability.Draft => "Brouillon : pas encore publié",
        FormAvailability.Closed => "Fermé",
        FormAvailability.NotYetOpen => "Publié, pas encore ouvert",
        FormAvailability.Ended => "Publié, date de fermeture passée",
        FormAvailability.Full => "Publié, nombre maximal de réponses atteint",
        _ => availability.ToString(),
    };

    private async Task<IActionResult> RespondAsync(
        Guid formId,
        LifecycleResult result,
        string done,
        CancellationToken cancellationToken,
        bool keepInputs = false) =>
        result.Status switch
        {
            LifecycleStatus.Ok => RedirectToPage(new { formId, fait = done }),
            LifecycleStatus.Invalid => await ShowAsync(formId, result.Error, cancellationToken, keepInputs),
            LifecycleStatus.Forbidden => Forbid(),
            _ => NotFound(),
        };

    private async Task<IActionResult> ShowAsync(Guid formId, string? error, CancellationToken cancellationToken, bool keepInputs = false)
    {
        var result = await service.GetPageAsync(User.ToCurrentUser(), formId, cancellationToken);
        switch (result.Status)
        {
            case LifecycleStatus.Ok:
                Form = result.Value!;
                Error = error;
                if (error is not null)
                {
                    Done = null;
                }

                if (!keepInputs)
                {
                    OpensAtInput = LocalInput(Form.OpensAt);
                    ClosesAtInput = LocalInput(Form.ClosesAt);
                    MaxResponsesInput = Form.MaxResponses?.ToString(CultureInfo.InvariantCulture);
                }

                return Page();
            case LifecycleStatus.Forbidden:
                return Forbid();
            default:
                return NotFound();
        }
    }

    /// <summary>Champ vide : pas de borne. Les secondes éventuelles (certains navigateurs) sont acceptées.</summary>
    private static bool TryParseLocal(string? input, out DateTime? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        if (DateTime.TryParseExact(
                input.Trim(),
                [LocalInputFormat, "yyyy-MM-dd'T'HH:mm:ss"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryParseCount(string? input, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        if (int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
