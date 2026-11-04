using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CseForms.Application.Common;
using CseForms.Domain.Common;
using CseForms.Domain.Forms;
using Microsoft.Extensions.Logging;

namespace CseForms.Application.FormAdministration;

/// <summary>
/// Création et publication des formulaires depuis un fichier de formulaire, et gestion du code d'accès.
/// Publier une définition modifiée crée toujours une nouvelle version ; les réponses existantes
/// restent rattachées à la version qu'elles ont remplie.
/// </summary>
public sealed partial class FormAdminService(
    IFormAdminStore store,
    IAccessCodeHasher accessCodeHasher,
    TimeProvider timeProvider,
    ILogger<FormAdminService> logger)
{
    /// <summary>Code imprimé sur le flyer et saisi au clavier numérique d'un smartphone.</summary>
    public const string AccessCodeRule = "4 à 12 chiffres";

    public static bool IsValidAccessCode(string? code) => code is not null && AccessCodePattern().IsMatch(code);

    public async Task<ImportOutcome> ImportAsync(FormPackage package, string? accessCode, string publishedBy, CancellationToken cancellationToken)
    {
        accessCode = accessCode?.Trim();
        if (!string.IsNullOrEmpty(accessCode) && !IsValidAccessCode(accessCode))
        {
            return new ImportOutcome.Invalid($"Le code d'accès doit comporter {AccessCodeRule}.");
        }

        var now = timeProvider.GetUtcNow();
        var form = await store.FindBySlugAsync(package.Slug.ToLowerInvariant(), cancellationToken);
        var isNew = form is null;

        try
        {
            form ??= new Form(package.Slug, package.Title, now);
            form.Rename(package.Title);
            form.SetCaseNumberPrefix(package.CaseNumberPrefix ?? Form.DefaultCaseNumberPrefix);
            form.SetRetentionDays(package.RetentionDays);
        }
        catch (DomainException ex)
        {
            return new ImportOutcome.Invalid(ex.Message);
        }

        switch (package.AccessMode)
        {
            case AccessMode.Pin when !string.IsNullOrEmpty(accessCode):
                form.SetAccessCodeHash(accessCodeHasher.Hash(accessCode));
                break;
            case AccessMode.Pin when form.AccessCodeHash is null:
                // Jamais de publication d'un formulaire « pin » sans code : il serait ouvert à tous.
                return new ImportOutcome.AccessCodeRequired();
            case AccessMode.Public:
                form.SetAccessCodeHash(null);
                break;
        }

        var current = form.CurrentVersion;
        var unchanged = current is not null && SameJson(current.SchemaJson, package.SchemaJson) && SameJson(current.UiJson, package.UiJson);
        var version = current;
        if (!unchanged)
        {
            var wasClosed = form.Status == FormStatus.Closed;
            version = form.Publish(package.SchemaJson, package.UiJson, current?.PdfTemplateHtml, now, publishedBy);

            // Une nouvelle version ne rouvre pas un formulaire fermé.
            if (wasClosed)
            {
                form.Close();
            }
        }

        if (isNew)
        {
            store.Add(form);
        }

        await store.SaveChangesAsync(cancellationToken);

        if (!unchanged)
        {
            LogPublished(form.Id, version!.Number);
        }

        return unchanged
            ? new ImportOutcome.Unchanged(form.Slug, version!.Number)
            : new ImportOutcome.Published(form.Slug, version!.Number, isNew);
    }

    public async Task<AccessCodeOutcome> SetAccessCodeAsync(string slug, string accessCode, CancellationToken cancellationToken)
    {
        accessCode = (accessCode ?? string.Empty).Trim();
        if (!IsValidAccessCode(accessCode))
        {
            return AccessCodeOutcome.Invalid;
        }

        var form = await store.FindBySlugAsync((slug ?? string.Empty).Trim().ToLowerInvariant(), cancellationToken);
        if (form is null)
        {
            return AccessCodeOutcome.NotFound;
        }

        form.SetAccessCodeHash(accessCodeHasher.Hash(accessCode));
        await store.SaveChangesAsync(cancellationToken);
        LogAccessCodeChanged(form.Id);
        return AccessCodeOutcome.Updated;
    }

    public Task<IReadOnlyList<FormListItem>> ListAsync(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);

    /// <summary>Comparaison du contenu : la colonne jsonb ne conserve ni l'ordre des clés ni la mise en forme.</summary>
    private static bool SameJson(string? stored, string? candidate) =>
        stored is null || candidate is null
            ? stored is null && candidate is null
            : JsonNode.DeepEquals(JsonNode.Parse(stored), JsonNode.Parse(candidate));

    [GeneratedRegex(@"^\d{4,12}$")]
    private static partial Regex AccessCodePattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "Formulaire {FormId} publié en version {VersionNumber}")]
    private partial void LogPublished(Guid formId, int versionNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Code d'accès du formulaire {FormId} modifié")]
    private partial void LogAccessCodeChanged(Guid formId);
}

public interface IFormAdminStore
{
    /// <summary>Formulaire suivi pour modification, avec ses versions ; null s'il n'existe pas.</summary>
    Task<Form?> FindBySlugAsync(string slug, CancellationToken cancellationToken);

    void Add(Form form);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<FormListItem>> ListAsync(CancellationToken cancellationToken);
}

public sealed record FormListItem(string Slug, string Title, FormStatus Status, int? Version, string CaseNumberPrefix, bool HasAccessCode, int? RetentionDays);

public abstract record ImportOutcome
{
    public sealed record Published(string Slug, int Version, bool Created) : ImportOutcome;

    public sealed record Unchanged(string Slug, int Version) : ImportOutcome;

    public sealed record AccessCodeRequired : ImportOutcome;

    public sealed record Invalid(string Message) : ImportOutcome;
}

public enum AccessCodeOutcome
{
    Updated,
    NotFound,
    Invalid,
}
