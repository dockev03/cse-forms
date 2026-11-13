using System.Text.Json;
using CseForms.Api.BackOffice;
using CseForms.Application.FormAdministration;
using CseForms.Application.Notifications;
using CseForms.Application.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Brouillon au format de l'API du formulaire public, lu par le cadre de l'aperçu. Mêmes droits que la page
/// d'aperçu (404 sans droit sur le formulaire, refus pour un lecteur) ; jamais mis en cache (en-têtes de <c>/admin</c>).
/// </summary>
public sealed class PreviewDataModel(FormDraftService service, ISettingReader settings) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid formId, CancellationToken cancellationToken)
    {
        var result = await service.GetPreviewAsync(User.ToCurrentUser(), formId, cancellationToken);
        switch (result.Status)
        {
            case LifecycleStatus.Forbidden:
                return Forbid();
            case not LifecycleStatus.Ok:
                return NotFound();
        }

        if (result.Value is not { DefinitionJson: { } definition } draft)
        {
            return NotFound();
        }

        var values = await settings.GetAllAsync(cancellationToken);
        return new JsonResult(new PreviewResponse(
            draft.Slug,
            draft.Title,
            Version: 0,
            Accepting: true,
            draft.RequiresAccessCode,
            Parse(definition),
            draft.UiJson is null ? null : Parse(draft.UiJson),
            draft.Sites,
            CseLogo.IsValid(values.GetValueOrDefault(SettingKeys.CseLogo)),
            values.GetValueOrDefault(SettingKeys.RgpdNotice) is { Length: > 0 },
            Preview: true));
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Mêmes champs que la réponse de <c>/api/public/forms/{slug}</c>, plus <c>preview</c>.</summary>
    public sealed record PreviewResponse(
        string Slug,
        string Title,
        int Version,
        bool Accepting,
        bool RequiresAccessCode,
        JsonElement Definition,
        JsonElement? Ui,
        IReadOnlyList<string> Sites,
        bool Logo,
        bool PrivacyNotice,
        bool Preview);
}
