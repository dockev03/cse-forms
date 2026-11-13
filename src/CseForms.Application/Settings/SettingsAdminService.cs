using CseForms.Application.BackOffice;
using CseForms.Application.Notifications;
using CseForms.Domain.Common;
using CseForms.Domain.Settings;
using CseForms.Domain.Sites;

namespace CseForms.Application.Settings;

/// <summary>
/// Paramètres de l'application dans le back-office, réservés aux administrateurs : coordonnées et bureau du CSE,
/// logo, adresse publique, adresse d'alerte, apparence du formulaire public, notice RGPD, liste des sites. Mêmes contrôles que la commande <c>settings</c>.
/// Le journal garde le nom des paramètres modifiés, jamais leur valeur.
/// </summary>
public sealed class SettingsAdminService(ISettingReader reader, ISettingsAdminStore store, IAuditLog audit)
{
    /// <summary>Paramètres modifiables depuis l'écran, dans l'ordre d'affichage. Les images ont leur propre envoi.</summary>
    public static readonly IReadOnlyList<string> EditableKeys =
    [
        SettingKeys.CseName,
        SettingKeys.CseAddress,
        SettingKeys.CseEmail,
        SettingKeys.CsePhone,
        SettingKeys.CseOffice,
        SettingKeys.PublicBaseUrl,
        SettingKeys.AlertsEmail,
        SettingKeys.ThemePrimaryColor,
        SettingKeys.RgpdNotice,
    ];

    public async Task<SettingsResult<SettingsPage>> GetPageAsync(CurrentUser user, CancellationToken cancellationToken)
    {
        if (await DenyAsync(user, cancellationToken))
        {
            return new(SettingsStatus.Forbidden);
        }

        var values = await reader.GetAllAsync(cancellationToken);
        return new(SettingsStatus.Ok, new SettingsPage(
            EditableKeys.ToDictionary(k => k, k => values.TryGetValue(k, out var v) ? v : null),
            ValidImage(values, SettingKeys.CseLogo),
            ValidImage(values, SettingKeys.ThemeBackground),
            await store.ListSitesAsync(cancellationToken)));
    }

    /// <summary>
    /// Enregistre tous les paramètres de l'écran d'un coup, ou aucun si l'un est refusé. Une valeur vide
    /// supprime le paramètre. Les clés absentes de <paramref name="values"/> ne sont pas touchées.
    /// </summary>
    public async Task<SettingsResult> SaveAsync(CurrentUser user, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken)
    {
        if (await DenyAsync(user, cancellationToken))
        {
            return new(SettingsStatus.Forbidden);
        }

        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in EditableKeys.Where(values.ContainsKey))
        {
            var value = SettingKeys.Normalize(key, values[key]);
            if (value.Length > 0 && SettingKeys.Validate(key, value) is { } error)
            {
                errors[key] = error;
            }

            normalized[key] = value;
        }

        if (errors.Count > 0)
        {
            return new(SettingsStatus.Invalid, errors);
        }

        var changed = new List<string>();
        foreach (var (key, value) in normalized)
        {
            var setting = await store.FindAsync(key, cancellationToken);
            if (value.Length == 0)
            {
                if (setting is not null)
                {
                    store.Remove(setting);
                    changed.Add(key);
                }
            }
            else if (setting is null)
            {
                store.Add(new Setting(key, value));
                changed.Add(key);
            }
            else if (setting.Value != value)
            {
                setting.Update(value);
                changed.Add(key);
            }
        }

        if (changed.Count > 0)
        {
            await store.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(AuditActions.SettingsChanged, user.Id, "settings", string.Join(',', changed), cancellationToken);
        }

        return SettingsResult.Ok;
    }

    /// <summary>Logo du CSE : contenu du fichier envoyé, contrôlé sur ses premiers octets (PNG ou JPEG), pas sur son nom.</summary>
    public Task<SettingsResult> SaveLogoAsync(CurrentUser user, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        SaveImageAsync(user, SettingKeys.CseLogo, "Logo refusé", content, cancellationToken);

    public Task<SettingsResult> RemoveLogoAsync(CurrentUser user, CancellationToken cancellationToken) =>
        RemoveImageAsync(user, SettingKeys.CseLogo, cancellationToken);

    /// <summary>Image de fond du formulaire public, mêmes contrôles que le logo.</summary>
    public Task<SettingsResult> SaveBackgroundAsync(CurrentUser user, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        SaveImageAsync(user, SettingKeys.ThemeBackground, "Image de fond refusée", content, cancellationToken);

    public Task<SettingsResult> RemoveBackgroundAsync(CurrentUser user, CancellationToken cancellationToken) =>
        RemoveImageAsync(user, SettingKeys.ThemeBackground, cancellationToken);

    /// <summary>Nouveau site, actif. Deux sites ne peuvent porter le même libellé, majuscules comprises.</summary>
    public async Task<SettingsResult> AddSiteAsync(CurrentUser user, string? label, CancellationToken cancellationToken)
    {
        if (await DenyAsync(user, cancellationToken))
        {
            return new(SettingsStatus.Forbidden);
        }

        Site site;
        try
        {
            site = new Site(label ?? string.Empty);
        }
        catch (DomainException ex)
        {
            return SettingsResult.Invalid(SiteField, ex.Message);
        }

        if (await store.SiteLabelExistsAsync(site.Label, cancellationToken))
        {
            return SettingsResult.Invalid(SiteField, $"Le site « {site.Label} » existe déjà.");
        }

        store.AddSite(site);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.SiteAdded, user.Id, SiteTarget(site.Id), cancellationToken);
        return SettingsResult.Ok;
    }

    /// <summary>
    /// Un site n'est jamais supprimé : désactivé, il sort des listes du formulaire, mais les réponses
    /// existantes le gardent. Ses QR codes déjà imprimés ne préremplissent plus le lieu de travail.
    /// </summary>
    public async Task<SettingsResult> SetSiteActiveAsync(CurrentUser user, Guid siteId, bool active, CancellationToken cancellationToken)
    {
        if (await DenyAsync(user, cancellationToken))
        {
            return new(SettingsStatus.Forbidden);
        }

        if (await store.FindSiteAsync(siteId, cancellationToken) is not { } site)
        {
            return new(SettingsStatus.NotFound);
        }

        if (site.IsActive == active)
        {
            return SettingsResult.Ok;
        }

        if (active)
        {
            site.Activate();
        }
        else
        {
            site.Deactivate();
        }

        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(active ? AuditActions.SiteEnabled : AuditActions.SiteDisabled, user.Id, SiteTarget(site.Id), cancellationToken);
        return SettingsResult.Ok;
    }

    private async Task<SettingsResult> SaveImageAsync(CurrentUser user, string key, string refusal, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (await DenyAsync(user, cancellationToken))
        {
            return new(SettingsStatus.Forbidden);
        }

        var image = SettingKeys.ImageKeys[key];
        if (image.ToDataUri(content.Span) is not { } dataUri)
        {
            return SettingsResult.Invalid(key, $"{refusal} : {image.Rule}.");
        }

        var setting = await store.FindAsync(key, cancellationToken);
        if (setting is null)
        {
            store.Add(new Setting(key, dataUri));
        }
        else
        {
            setting.Update(dataUri);
        }

        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.SettingsChanged, user.Id, "settings", key, cancellationToken);
        return SettingsResult.Ok;
    }

    private async Task<SettingsResult> RemoveImageAsync(CurrentUser user, string key, CancellationToken cancellationToken)
    {
        if (await DenyAsync(user, cancellationToken))
        {
            return new(SettingsStatus.Forbidden);
        }

        if (await store.FindAsync(key, cancellationToken) is { } setting)
        {
            store.Remove(setting);
            await store.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(AuditActions.SettingsChanged, user.Id, "settings", key, cancellationToken);
        }

        return SettingsResult.Ok;
    }

    private static string? ValidImage(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && SettingKeys.ImageKeys[key].IsValid(value) ? value : null;

    /// <summary>Nom du champ d'erreur pour l'ajout d'un site.</summary>
    public const string SiteField = "site";

    private static string SiteTarget(Guid id) => $"site:{id}";

    /// <summary>Les pages vérifient déjà le rôle ; ce contrôle protège le cas d'usage s'il est appelé d'ailleurs.</summary>
    private async Task<bool> DenyAsync(CurrentUser user, CancellationToken cancellationToken)
    {
        if (user.IsAdmin)
        {
            return false;
        }

        await audit.RecordAsync(AuditActions.AccessDenied, user.Id, "settings", cancellationToken);
        return true;
    }
}

public interface ISettingsAdminStore
{
    /// <summary>Paramètre suivi pour modification ; null s'il n'est pas défini.</summary>
    Task<Setting?> FindAsync(string key, CancellationToken cancellationToken);

    void Add(Setting setting);

    void Remove(Setting setting);

    /// <summary>Sites triés par libellé, actifs et inactifs.</summary>
    Task<IReadOnlyList<SiteView>> ListSitesAsync(CancellationToken cancellationToken);

    Task<Site?> FindSiteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Vrai si un site porte déjà ce libellé, sans tenir compte des majuscules.</summary>
    Task<bool> SiteLabelExistsAsync(string label, CancellationToken cancellationToken);

    void AddSite(Site site);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record SiteView(Guid Id, string Label, bool IsActive);

/// <param name="Values">Valeur de chaque paramètre de <see cref="SettingsAdminService.EditableKeys"/> ; null s'il n'est pas défini.</param>
/// <param name="Logo">Adresse <c>data:</c> du logo ; null s'il n'est pas défini.</param>
/// <param name="Background">Adresse <c>data:</c> de l'image de fond du formulaire public ; null si elle n'est pas définie.</param>
public sealed record SettingsPage(IReadOnlyDictionary<string, string?> Values, string? Logo, string? Background, IReadOnlyList<SiteView> Sites);

public enum SettingsStatus
{
    Ok,
    NotFound,
    Forbidden,
    Invalid,
}

/// <param name="Errors">Message d'erreur par paramètre (ou <see cref="SettingsAdminService.SiteField"/>).</param>
public sealed record SettingsResult(SettingsStatus Status, IReadOnlyDictionary<string, string>? Errors = null)
{
    public static readonly SettingsResult Ok = new(SettingsStatus.Ok);

    public static SettingsResult Invalid(string field, string error) => new(SettingsStatus.Invalid, new Dictionary<string, string> { [field] = error });
}

public sealed record SettingsResult<T>(SettingsStatus Status, T? Value = default);
