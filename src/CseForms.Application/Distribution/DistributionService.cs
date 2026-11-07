using System.Text;
using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.Notifications;
using CseForms.Application.Pdf;
using CseForms.Application.Settings;

namespace CseForms.Application.Distribution;

/// <summary>
/// Diffusion d'un formulaire : lien public, QR code (PNG, SVG), un QR code par site, flyer PDF.
/// Réservée aux administrateurs et éditeurs ayant le droit sur le formulaire. Le code d'accès imprimé
/// sur le flyer est saisi par l'élu, vérifié contre le haché et jamais conservé.
/// </summary>
public sealed class DistributionService(
    IBackOfficeStore backOffice,
    IDistributionStore store,
    IAuditLog audit,
    IQrCodeRenderer qrCodes,
    IPdfConverter converter,
    IAccessCodeHasher accessCodeHasher,
    ISettingReader settings)
{
    public const string MissingBaseUrl = "L'adresse publique du site n'est pas définie (paramètre app.publicBaseUrl).";

    public async Task<DistributionResult<DistributionPage>> GetPageAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var (status, form) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != DistributionStatus.Ok)
        {
            return new(status);
        }

        var values = await settings.GetAllAsync(cancellationToken);
        var siteParameter = FormLink.SiteParameter(form!.CurrentSchemaJson);
        return DistributionResult<DistributionPage>.Ok(new DistributionPage(
            form.Id,
            form.Slug,
            form.Title,
            form.Status,
            BaseUrl(values) is { } baseUrl ? FormLink.Build(baseUrl, form.Slug) : null,
            siteParameter,
            siteParameter is null ? [] : await store.GetActiveSiteLabelsAsync(cancellationToken),
            form.AccessCodeHash is not null));
    }

    public async Task<DistributionResult<DistributionFile>> QrCodeAsync(
        CurrentUser user,
        Guid formId,
        string? site,
        QrFormat format,
        CancellationToken cancellationToken)
    {
        var (status, form) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != DistributionStatus.Ok)
        {
            return new(status);
        }

        var link = await LinkAsync(form!, site, cancellationToken);
        if (link.Error is not null)
        {
            return DistributionResult<DistributionFile>.Invalid(link.Error);
        }

        var name = FileName("qr", form!.Slug, link.Site);
        var file = format == QrFormat.Svg
            ? new DistributionFile($"{name}.svg", "image/svg+xml", Encoding.UTF8.GetBytes(qrCodes.Svg(link.Url!)))
            : new DistributionFile($"{name}.png", "image/png", qrCodes.Png(link.Url!));

        await audit.RecordAsync(AuditActions.QrCodeDownloaded, user.Id, $"form:{formId}", cancellationToken);
        return DistributionResult<DistributionFile>.Ok(file);
    }

    public async Task<DistributionResult<DistributionFile>> FlyerAsync(
        CurrentUser user,
        Guid formId,
        string? site,
        string? accessCode,
        CancellationToken cancellationToken)
    {
        var (status, form) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != DistributionStatus.Ok)
        {
            return new(status);
        }

        var code = accessCode?.Trim();
        if (form!.AccessCodeHash is null)
        {
            code = null;
        }
        else if (string.IsNullOrEmpty(code))
        {
            return DistributionResult<DistributionFile>.Invalid("Saisissez le code d'accès du formulaire : il est imprimé sur le flyer.");
        }
        else if (code.Length > PublicForms.PublicFormService.AccessCodeMaxLength || !accessCodeHasher.Verify(form.AccessCodeHash, code))
        {
            // Le code n'est pas retenu : il ne serait d'aucune aide et ne doit pas se retrouver dans un journal.
            await audit.RecordAsync(AuditActions.FlyerCodeRejected, user.Id, $"form:{formId}", cancellationToken);
            return DistributionResult<DistributionFile>.Invalid("Code d'accès incorrect : ce n'est pas celui du formulaire.");
        }

        var link = await LinkAsync(form, site, cancellationToken);
        if (link.Error is not null)
        {
            return DistributionResult<DistributionFile>.Invalid(link.Error);
        }

        var html = FlyerRenderer.Render(new FlyerContent(form.Title, link.Url!, qrCodes.Svg(link.Url!), link.Site, code, link.Settings!));
        var pdf = await converter.ConvertAsync(html, cancellationToken);

        await audit.RecordAsync(AuditActions.FlyerGenerated, user.Id, $"form:{formId}", cancellationToken);
        return DistributionResult<DistributionFile>.Ok(new DistributionFile($"{FileName("flyer", form.Slug, link.Site)}.pdf", "application/pdf", pdf));
    }

    /// <summary>Formulaire existant, accessible au compte, qui n'est pas en lecture seule ; un refus est journalisé.</summary>
    private async Task<(DistributionStatus Status, DistributionForm? Form)> AuthorizeAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var form = await store.GetFormAsync(formId, cancellationToken);
        if (form is null)
        {
            return (DistributionStatus.NotFound, null);
        }

        if (!await backOffice.CanAccessFormAsync(user, formId, cancellationToken))
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, $"form:{formId}", cancellationToken);
            return (DistributionStatus.NotFound, null);
        }

        if (user.IsReadOnly)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, $"form:{formId}", cancellationToken);
            return (DistributionStatus.Forbidden, null);
        }

        return (DistributionStatus.Ok, form);
    }

    private async Task<(string? Url, string? Site, IReadOnlyDictionary<string, string>? Settings, string? Error)> LinkAsync(
        DistributionForm form,
        string? site,
        CancellationToken cancellationToken)
    {
        var values = await settings.GetAllAsync(cancellationToken);
        if (BaseUrl(values) is not { } baseUrl)
        {
            return (null, null, null, MissingBaseUrl);
        }

        site = string.IsNullOrWhiteSpace(site) ? null : site.Trim();
        if (site is null)
        {
            return (FormLink.Build(baseUrl, form.Slug), null, values, null);
        }

        // Liste blanche : le formulaire public n'accepterait pas un autre site.
        var parameter = FormLink.SiteParameter(form.CurrentSchemaJson);
        var label = parameter is null
            ? null
            : (await store.GetActiveSiteLabelsAsync(cancellationToken)).FirstOrDefault(s => string.Equals(s, site, StringComparison.Ordinal));
        return label is null
            ? (null, null, null, "Site inconnu, inactif, ou formulaire sans lieu de travail préremplissable.")
            : (FormLink.Build(baseUrl, form.Slug, parameter, label), label, values, null);
    }

    private static string? BaseUrl(IReadOnlyDictionary<string, string> values) =>
        values.TryGetValue(SettingKeys.PublicBaseUrl, out var url) && !string.IsNullOrWhiteSpace(url) ? url.TrimEnd('/') : null;

    private static string FileName(string kind, string slug, string? site) =>
        site is null ? $"{kind}-{slug}" : $"{kind}-{slug}-{FormLink.FileNamePart(site)}";
}
