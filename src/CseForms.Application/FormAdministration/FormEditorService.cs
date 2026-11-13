using System.Text.Json;
using System.Text.Json.Nodes;
using CseForms.Application.BackOffice;
using CseForms.Application.FormDefinitions;
using CseForms.Domain.Forms;

namespace CseForms.Application.FormAdministration;

/// <summary>
/// Éditeur visuel : il charge un fichier de formulaire (brouillon, sinon version en ligne, sinon fichier vierge)
/// et l'enregistre comme brouillon. Il ne contrôle rien lui-même : l'enregistrement passe par
/// <see cref="FormDraftService.SaveAsync"/>, donc par les mêmes contrôles et les mêmes droits que l'import, et la
/// publication se fait depuis l'aperçu, comme pour un brouillon importé. Le modèle PDF n'est pas modifié par
/// l'éditeur : celui du brouillon (ou de la version en ligne) est conservé. L'adresse d'un formulaire existant
/// ne change pas ; créer un formulaire reste réservé aux administrateurs.
/// </summary>
public sealed class FormEditorService(
    IBackOfficeStore backOffice,
    IFormDraftStore store,
    FormDraftService drafts,
    IAuditLog audit)
{
    public const string SlugLocked = "L'adresse d'un formulaire existant ne se modifie pas : pour changer d'adresse, dupliquez le formulaire.";

    private const string DeniedDetail = "form-editor";

    /// <summary>Fichier de départ d'un nouveau formulaire ; null si l'accès est refusé (journalisé).</summary>
    public async Task<EditorDocument?> GetNewAsync(CurrentUser user, CancellationToken cancellationToken)
    {
        if (!user.IsAdmin)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, null, DeniedDetail, cancellationToken);
            return null;
        }

        return new EditorDocument(null, null, BlankPackage(string.Empty, string.Empty), EditorSource.New, HasPdfTemplate: false, []);
    }

    /// <summary>Fichier à modifier : brouillon, sinon version en ligne, sinon fichier vierge à l'adresse du formulaire.</summary>
    public async Task<LifecycleResult<EditorDocument>> GetAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var (status, form) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != LifecycleStatus.Ok)
        {
            return new(status);
        }

        var published = PublishedFieldIds(form!.CurrentVersion);
        var draft = await store.FindDraftAsync(formId, cancellationToken);
        if (draft is not null)
        {
            return new(LifecycleStatus.Ok, new EditorDocument(form.Id, form.Slug, draft.PackageJson, EditorSource.Draft, draft.PdfTemplateHtml is not null, published));
        }

        if (form.CurrentVersion is { } version)
        {
            return new(LifecycleStatus.Ok, new EditorDocument(
                form.Id,
                form.Slug,
                FormDuplicationService.BuildPackage(form, version, form.Slug, form.Title),
                EditorSource.Version,
                version.PdfTemplateHtml is not null,
                published));
        }

        return new(LifecycleStatus.Ok, new EditorDocument(form.Id, form.Slug, BlankPackage(form.Slug, form.Title), EditorSource.New, HasPdfTemplate: false, published));
    }

    /// <summary>
    /// Enregistre le fichier produit par l'éditeur comme brouillon : du formulaire <paramref name="formId"/>, ou
    /// d'un nouveau formulaire si <paramref name="formId"/> est null. Rien n'est publié.
    /// </summary>
    public async Task<EditorSaveResult> SaveAsync(CurrentUser user, Guid? formId, string packageJson, CancellationToken cancellationToken)
    {
        string? pdfTemplate = null;
        var slug = ReadSlug(packageJson);
        if (formId is { } id)
        {
            var (status, form) = await AuthorizeAsync(user, id, cancellationToken);
            switch (status)
            {
                case LifecycleStatus.NotFound:
                    return new EditorSaveResult.NotFound();
                case LifecycleStatus.Forbidden:
                    return new EditorSaveResult.Forbidden();
            }

            // Le slug désigne le formulaire dans le fichier : il ne peut pas viser un autre formulaire.
            if (slug is not null && !string.Equals(slug, form!.Slug, StringComparison.OrdinalIgnoreCase))
            {
                return Invalid("$.slug", SlugLocked);
            }

            var draft = await store.FindDraftAsync(id, cancellationToken);
            pdfTemplate = draft is not null ? draft.PdfTemplateHtml : form!.CurrentVersion?.PdfTemplateHtml;
        }
        else
        {
            if (!user.IsAdmin)
            {
                await audit.RecordAsync(AuditActions.AccessDenied, user.Id, null, DeniedDetail, cancellationToken);
                return new EditorSaveResult.Forbidden();
            }

            // Un nouveau formulaire ne remplace jamais le brouillon d'un formulaire existant.
            if (slug is not null && await store.FindFormBySlugAsync(slug.ToLowerInvariant(), cancellationToken) is not null)
            {
                return Invalid("$.slug", FormDuplicationService.SlugTaken);
            }
        }

        return await drafts.SaveAsync(user, packageJson, pdfTemplate, fromEditor: true, cancellationToken) switch
        {
            DraftSaveResult.Saved saved => new EditorSaveResult.Saved(saved.FormId),
            DraftSaveResult.Invalid invalid => new EditorSaveResult.Invalid(invalid.Errors),
            _ => new EditorSaveResult.Forbidden(),
        };
    }

    /// <summary>Fichier vierge : une section, aucun champ ; conservation de 180 jours proposée par défaut.</summary>
    public static string BlankPackage(string slug, string title) =>
        new JsonObject
        {
            ["slug"] = slug,
            ["title"] = title,
            ["caseNumberPrefix"] = "DOS",
            ["retentionDays"] = 180,
            ["access"] = new JsonObject { ["mode"] = "public" },
            ["sections"] = new JsonArray(new JsonObject { ["id"] = "section1", ["title"] = "Section 1", ["fields"] = new JsonArray() }),
        }.ToJsonString();

    /// <summary>Identifiants des champs de la version en ligne (y compris ceux des groupes), pour avertir avant un renommage.</summary>
    private static IReadOnlyList<string> PublishedFieldIds(FormVersion? version) =>
        version is null
            ? []
            : FormDefinitionParser.Parse(version.SchemaJson).Definition?.AllTopLevelFields
                .SelectMany(f => f.Fields is null ? [f] : f.Fields.Prepend(f))
                .Select(f => f.Id)
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? [];

    /// <summary>Slug du fichier, null s'il est absent ou illisible : le contrôle de l'import le signalera.</summary>
    private static string? ReadSlug(string packageJson)
    {
        try
        {
            return JsonNode.Parse(packageJson) is JsonObject root && root["slug"] is JsonValue value && value.TryGetValue<string>(out var slug)
                ? slug.Trim()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Mêmes règles que l'aperçu : 404 sans droit sur le formulaire, 403 pour un lecteur, refus journalisés.</summary>
    private async Task<(LifecycleStatus Status, Form? Form)> AuthorizeAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var form = await store.FindFormAsync(formId, cancellationToken);
        if (form is null)
        {
            return (LifecycleStatus.NotFound, null);
        }

        if (!await backOffice.CanAccessFormAsync(user, formId, cancellationToken))
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, $"form:{formId}", DeniedDetail, cancellationToken);
            return (LifecycleStatus.NotFound, null);
        }

        if (user.IsReadOnly)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, $"form:{formId}", DeniedDetail, cancellationToken);
            return (LifecycleStatus.Forbidden, null);
        }

        return (LifecycleStatus.Ok, form);
    }

    private static EditorSaveResult.Invalid Invalid(string path, string message) => new([new DefinitionError(path, message)]);
}

public enum EditorSource
{
    /// <summary>Fichier vierge (nouveau formulaire, ou formulaire sans version ni brouillon).</summary>
    New,

    /// <summary>Brouillon en cours.</summary>
    Draft,

    /// <summary>Version en ligne, reconstruite en fichier de formulaire.</summary>
    Version,
}

/// <param name="FormId">Null pour un nouveau formulaire.</param>
/// <param name="Slug">Adresse figée d'un formulaire existant ; null pour un nouveau formulaire.</param>
/// <param name="HasPdfTemplate">Le formulaire a un modèle PDF personnalisé, conservé à l'enregistrement.</param>
/// <param name="PublishedFieldIds">Identifiants des champs de la version en ligne : les renommer sépare les nouvelles réponses des anciennes.</param>
public sealed record EditorDocument(
    Guid? FormId,
    string? Slug,
    string PackageJson,
    EditorSource Source,
    bool HasPdfTemplate,
    IReadOnlyList<string> PublishedFieldIds);

public abstract record EditorSaveResult
{
    public sealed record Saved(Guid FormId) : EditorSaveResult;

    public sealed record Invalid(IReadOnlyList<DefinitionError> Errors) : EditorSaveResult;

    public sealed record NotFound : EditorSaveResult;

    public sealed record Forbidden : EditorSaveResult;
}
