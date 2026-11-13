using CseForms.Api.BackOffice;
using CseForms.Application.Settings;
using Microsoft.AspNetCore.Mvc;

namespace CseForms.Api.Pages.Admin;

/// <summary>
/// Paramètres de l'application : coordonnées et bureau du CSE, adresse publique, adresse d'alerte, logo,
/// apparence du formulaire public (couleur, image de fond), sites.
/// Réservé aux administrateurs (404 pour les autres rôles).
/// </summary>
[RequestSizeLimit(MaxRequestBytes)]
public sealed class SettingsModel(SettingsAdminService service) : AdminPageModel
{
    /// <summary>La plus grande image (fond), plus les champs du formulaire et l'enveloppe multipart.</summary>
    public const int MaxRequestBytes = BackgroundImage.MaxBytes + (64 * 1024);

    public static readonly IReadOnlyDictionary<string, (string Label, string Help)> Fields = new Dictionary<string, (string, string)>
    {
        [SettingKeys.CseName] = ("Nom du CSE", "Affiché sur les PDF, le flyer et les emails."),
        [SettingKeys.CseAddress] = ("Adresse postale", "Plusieurs lignes possibles."),
        [SettingKeys.CseEmail] = ("Email de contact", "Communiqué aux salariés (flyer, PDF). Préférez une boîte du CSE à une adresse personnelle."),
        [SettingKeys.CsePhone] = ("Téléphone de contact", string.Empty),
        [SettingKeys.CseOffice] = ("Composition du bureau", "Une ligne par membre, par exemple « Secrétaire : Prénom Nom ». Reprise sur les PDF à la place des modèles."),
        [SettingKeys.PublicBaseUrl] = ("Adresse publique du site", "Adresse HTTPS sans chemin, par exemple https://formulaires.example.test. Sert aux liens des emails, au QR code et au flyer : la changer rend inutilisables les QR codes déjà imprimés."),
        [SettingKeys.AlertsEmail] = ("Adresse d'alerte", "Reçoit les alertes d'échec définitif des tâches (PDF, emails), sans donnée saisie."),
    };

    /// <summary>Libellé d'un champ dans le récapitulatif des erreurs.</summary>
    public static string Label(string key) => Fields.TryGetValue(key, out var field) ? field.Label : key switch
    {
        SettingKeys.CseLogo => "Logo",
        SettingKeys.ThemePrimaryColor => "Couleur principale",
        SettingKeys.ThemeBackground => "Image de fond",
        _ => "Site",
    };

    public SettingsPage Settings { get; private set; } = null!;

    public IReadOnlyDictionary<string, string> Errors { get; private set; } = new Dictionary<string, string>();

    /// <summary>Saisie affichée : celle refusée après une erreur, sinon les valeurs enregistrées.</summary>
    public Dictionary<string, string?> Inputs { get; private set; } = [];

    public string? SiteInput { get; private set; }

    [BindProperty(SupportsGet = true, Name = "fait")]
    public string? Done { get; set; }

    public string? Message => Done switch
    {
        "parametres" => "Paramètres enregistrés.",
        "logo" => "Logo enregistré.",
        "logo-retire" => "Logo retiré : il n'apparaît plus sur le formulaire public ni sur les prochains documents.",
        "couleur" => "Couleur enregistrée : elle s'applique aux formulaires publics.",
        "fond" => "Image de fond enregistrée.",
        "fond-retire" => "Image de fond retirée.",
        "site-ajoute" => "Site ajouté : il apparaît dans les formulaires.",
        "site-desactive" => "Site désactivé : il n'est plus proposé, les réponses existantes le gardent.",
        "site-active" => "Site réactivé.",
        _ => null,
    };

    public static string FieldId(string key) => "p-" + key.Replace('.', '-');

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) => await ShowAsync(cancellationToken);

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var values = SettingsAdminService.EditableKeys
            .Where(k => k != SettingKeys.ThemePrimaryColor && Request.Form.ContainsKey(k))
            .ToDictionary(k => k, k => (string?)Request.Form[k].ToString());
        var result = await service.SaveAsync(User.ToCurrentUser(), values, cancellationToken);
        return await RespondAsync(result, "parametres", cancellationToken, keep: () => Inputs = new(values));
    }

    /// <summary>Couleur principale seule : vide, elle revient à la couleur par défaut.</summary>
    public async Task<IActionResult> OnPostThemeAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string?> { [SettingKeys.ThemePrimaryColor] = Request.Form[SettingKeys.ThemePrimaryColor].ToString() };
        var result = await service.SaveAsync(User.ToCurrentUser(), values, cancellationToken);
        return await RespondAsync(result, "couleur", cancellationToken, keep: () => Inputs = new(values));
    }

    public async Task<IActionResult> OnPostLogoAsync(IFormFile? logo, CancellationToken cancellationToken)
    {
        // Taille contrôlée avant lecture ; le contenu est vérifié par le service, pas l'extension ni le type annoncé.
        if (logo is null || logo.Length == 0 || logo.Length > CseLogo.MaxBytes)
        {
            return await RespondAsync(SettingsResult.Invalid(SettingKeys.CseLogo, $"Logo refusé : {CseLogo.Rule}."), "logo", cancellationToken);
        }

        using var buffer = new MemoryStream((int)logo.Length);
        await logo.CopyToAsync(buffer, cancellationToken);
        return await RespondAsync(await service.SaveLogoAsync(User.ToCurrentUser(), buffer.ToArray(), cancellationToken), "logo", cancellationToken);
    }

    public async Task<IActionResult> OnPostRemoveLogoAsync(CancellationToken cancellationToken) =>
        await RespondAsync(await service.RemoveLogoAsync(User.ToCurrentUser(), cancellationToken), "logo-retire", cancellationToken);

    public async Task<IActionResult> OnPostBackgroundAsync(IFormFile? background, CancellationToken cancellationToken)
    {
        if (background is null || background.Length == 0 || background.Length > BackgroundImage.MaxBytes)
        {
            return await RespondAsync(SettingsResult.Invalid(SettingKeys.ThemeBackground, $"Image de fond refusée : {BackgroundImage.Rule}."), "fond", cancellationToken);
        }

        using var buffer = new MemoryStream((int)background.Length);
        await background.CopyToAsync(buffer, cancellationToken);
        return await RespondAsync(await service.SaveBackgroundAsync(User.ToCurrentUser(), buffer.ToArray(), cancellationToken), "fond", cancellationToken);
    }

    public async Task<IActionResult> OnPostRemoveBackgroundAsync(CancellationToken cancellationToken) =>
        await RespondAsync(await service.RemoveBackgroundAsync(User.ToCurrentUser(), cancellationToken), "fond-retire", cancellationToken);

    public async Task<IActionResult> OnPostAddSiteAsync([FromForm] string? label, CancellationToken cancellationToken) =>
        await RespondAsync(await service.AddSiteAsync(User.ToCurrentUser(), label, cancellationToken), "site-ajoute", cancellationToken, keep: () => SiteInput = label);

    public async Task<IActionResult> OnPostDisableSiteAsync([FromForm] Guid siteId, CancellationToken cancellationToken) =>
        await RespondAsync(await service.SetSiteActiveAsync(User.ToCurrentUser(), siteId, active: false, cancellationToken), "site-desactive", cancellationToken);

    public async Task<IActionResult> OnPostEnableSiteAsync([FromForm] Guid siteId, CancellationToken cancellationToken) =>
        await RespondAsync(await service.SetSiteActiveAsync(User.ToCurrentUser(), siteId, active: true, cancellationToken), "site-active", cancellationToken);

    private async Task<IActionResult> RespondAsync(SettingsResult result, string done, CancellationToken cancellationToken, Action? keep = null)
    {
        switch (result.Status)
        {
            case SettingsStatus.Ok:
                return RedirectToPage(new { fait = done });
            case SettingsStatus.Invalid:
                Errors = result.Errors!;
                Done = null;
                keep?.Invoke();
                return await ShowAsync(cancellationToken);
            case SettingsStatus.Forbidden:
                return Forbid();
            default:
                return NotFound();
        }
    }

    private async Task<IActionResult> ShowAsync(CancellationToken cancellationToken)
    {
        var result = await service.GetPageAsync(User.ToCurrentUser(), cancellationToken);
        if (result.Status != SettingsStatus.Ok)
        {
            return NotFound();
        }

        Settings = result.Value!;
        foreach (var (key, value) in Settings.Values)
        {
            Inputs.TryAdd(key, value);
        }

        return Page();
    }
}
