using System.Globalization;
using System.Text.Json.Nodes;
using CseForms.Application.BackOffice;
using CseForms.Application.FormDefinitions;
using CseForms.Domain.Common;
using CseForms.Domain.Forms;

namespace CseForms.Application.FormAdministration;

/// <summary>
/// Modèles de formulaires : catalogue livré avec l'application (<see cref="BuiltInFormTemplates"/>) et modèles
/// enregistrés par les administrateurs à partir de la version en ligne d'un formulaire (table <c>form_template</c>).
/// Partir d'un modèle crée un formulaire hors ligne, dont le modèle devient le brouillon, à vérifier dans l'aperçu
/// puis à publier. Un modèle ne porte ni réponse, ni règle de notification, ni compte attribué, ni code d'accès.
/// Tout est réservé aux administrateurs, comme la création d'un formulaire ; un refus est journalisé.
/// </summary>
public sealed class FormTemplateService(
    IFormTemplateStore store,
    IAuditLog audit,
    TimeProvider timeProvider)
{
    public const string NotPublished = "Ce formulaire n'a pas de version en ligne : seule une version publiée peut devenir un modèle.";

    public const string NameTaken = "Un modèle porte déjà ce nom.";

    private const string DeniedDetail = "form-template";

    /// <summary>Catalogue : modèles livrés, puis modèles enregistrés par nom ; null si l'accès est refusé (journalisé).</summary>
    public async Task<IReadOnlyList<TemplateSummary>?> ListAsync(CurrentUser user, CancellationToken cancellationToken)
    {
        if (!await AllowedAsync(user, null, cancellationToken))
        {
            return null;
        }

        var saved = await store.ListTemplatesAsync(cancellationToken);
        return BuiltInFormTemplates.All
            .Select(t => new TemplateSummary(t.Code, t.Name, t.Description, BuiltIn: true, null))
            .Concat(saved
                .OrderBy(t => t.Name, StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), ignoreCase: true))
                .Select(t => new TemplateSummary(Key(t), t.Name, t.Description, BuiltIn: false, t.CreatedAt)))
            .ToList();
    }

    /// <summary>Détail d'un modèle pour l'écran d'utilisation ; null s'il n'existe pas ou si l'accès est refusé (journalisé).</summary>
    public async Task<TemplateDetail?> GetAsync(CurrentUser user, string? key, CancellationToken cancellationToken)
    {
        var template = await FindAsync(key, cancellationToken);
        if (!await AllowedAsync(user, template?.AuditTarget, cancellationToken) || template is null)
        {
            return null;
        }

        var read = FormImportService.ReadUpload(template.PackageJson, template.PdfTemplateHtml);
        var package = read.Package;
        var sections = package is null
            ? []
            : FormDefinitionParser.Parse(package.SchemaJson).Definition?.Sections
                .Select(s => new TemplateSection(s.Title, s.Fields.Count(f => f.Type is not (FieldType.Info or FieldType.Hidden))))
                .ToList() ?? [];
        return new TemplateDetail(
            template.Key,
            template.Name,
            template.Description,
            template.BuiltIn,
            package?.Slug,
            package?.Title,
            package?.AccessMode,
            package?.PdfTemplateHtml is not null,
            package is not null && JsonNode.Parse(package.SchemaJson)?["receipt"] is not null,
            sections,
            read.Errors);
    }

    /// <summary>Crée un formulaire hors ligne dont le modèle, avec la nouvelle adresse et le nouveau titre, devient le brouillon.</summary>
    public async Task<TemplateUseResult> CreateFormAsync(CurrentUser user, string? key, string? slug, string? title, CancellationToken cancellationToken)
    {
        var template = await FindAsync(key, cancellationToken);
        if (!await AllowedAsync(user, template?.AuditTarget, cancellationToken))
        {
            return new TemplateUseResult.Forbidden();
        }

        if (template is null)
        {
            return new TemplateUseResult.NotFound();
        }

        var now = timeProvider.GetUtcNow();
        Form form;
        try
        {
            form = new Form((slug ?? string.Empty).Trim().ToLowerInvariant(), (title ?? string.Empty).Trim(), now);
        }
        catch (DomainException ex)
        {
            return new TemplateUseResult.Invalid([new DefinitionError(ex.Message.StartsWith("Le slug", StringComparison.Ordinal) ? "slug" : "title", ex.Message)]);
        }

        if (await store.SlugExistsAsync(form.Slug, cancellationToken))
        {
            return new TemplateUseResult.Invalid([new DefinitionError("slug", FormDuplicationService.SlugTaken)]);
        }

        // Le fichier du modèle, avec la nouvelle adresse et le nouveau titre, passe les mêmes contrôles qu'un import.
        var packageJson = Rename(template.PackageJson, form.Slug, form.Title);
        var read = packageJson is null
            ? new FormPackageResult(null, [new DefinitionError("$", "JSON invalide.")])
            : FormImportService.ReadUpload(packageJson, template.PdfTemplateHtml);
        if (read.Errors.Count > 0)
        {
            return new TemplateUseResult.Invalid(read.Errors);
        }

        store.AddForm(form);
        store.AddDraft(new FormDraft(form.Id, packageJson!, template.PdfTemplateHtml, now, $"user:{user.Id}"));
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(AuditActions.FormCreatedFromTemplate, user.Id, $"form:{form.Id}", $"template={template.AuditName}", cancellationToken);
        return new TemplateUseResult.Created(form.Id, form.Slug, read.Package!.AccessMode == AccessMode.Pin);
    }

    /// <summary>Formulaire à enregistrer comme modèle ; null s'il n'existe pas ou si l'accès est refusé (journalisé).</summary>
    public async Task<DuplicationSource?> GetSaveSourceAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var form = await store.FindFormAsync(formId, cancellationToken);
        if (!await AllowedAsync(user, form is null ? null : $"form:{form.Id}", cancellationToken) || form is null)
        {
            return null;
        }

        return new DuplicationSource(form.Id, form.Slug, form.Title, form.CurrentVersion?.Number, form.AccessCodeHash is not null);
    }

    /// <summary>Enregistre la version en ligne d'un formulaire comme modèle, sans réponse, notification, compte ni code d'accès.</summary>
    public async Task<TemplateSaveResult> SaveFromFormAsync(CurrentUser user, Guid formId, string? name, string? description, CancellationToken cancellationToken)
    {
        var form = await store.FindFormAsync(formId, cancellationToken);
        if (!await AllowedAsync(user, form is null ? null : $"form:{form.Id}", cancellationToken))
        {
            return new TemplateSaveResult.Forbidden();
        }

        if (form is null)
        {
            return new TemplateSaveResult.NotFound();
        }

        if (form.CurrentVersion is not { } version)
        {
            return new TemplateSaveResult.Invalid([new DefinitionError("source", NotPublished)]);
        }

        string normalizedName;
        string? normalizedDescription;
        try
        {
            normalizedName = FormTemplate.NormalizeName(name);
            normalizedDescription = FormTemplate.NormalizeDescription(description);
        }
        catch (DomainException ex)
        {
            return new TemplateSaveResult.Invalid([new DefinitionError(ex.Message.Contains("description", StringComparison.Ordinal) ? "description" : "name", ex.Message)]);
        }

        var saved = await store.ListTemplatesAsync(cancellationToken);
        if (BuiltInFormTemplates.All.Select(t => t.Name).Concat(saved.Select(t => t.Name))
            .Any(n => string.Equals(n, normalizedName, StringComparison.CurrentCultureIgnoreCase)))
        {
            return new TemplateSaveResult.Invalid([new DefinitionError("name", NameTaken)]);
        }

        var packageJson = FormDuplicationService.BuildPackage(form, version, form.Slug, form.Title);
        var read = FormImportService.ReadUpload(packageJson, version.PdfTemplateHtml);
        if (read.Errors.Count > 0)
        {
            return new TemplateSaveResult.Invalid(read.Errors);
        }

        var template = new FormTemplate(normalizedName, normalizedDescription, packageJson, version.PdfTemplateHtml, timeProvider.GetUtcNow(), $"user:{user.Id}");
        store.AddTemplate(template);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            AuditActions.FormTemplateSaved,
            user.Id,
            $"template:{template.Id}",
            $"from=form:{form.Id};v{version.Number.ToString(CultureInfo.InvariantCulture)}",
            cancellationToken);
        return new TemplateSaveResult.Saved(template.Id);
    }

    /// <summary>Modèle enregistré à supprimer ; null s'il n'existe pas ou si l'accès est refusé (journalisé).</summary>
    public async Task<TemplateSummary?> GetSavedAsync(CurrentUser user, Guid templateId, CancellationToken cancellationToken)
    {
        var template = await store.FindTemplateAsync(templateId, cancellationToken);
        if (!await AllowedAsync(user, template is null ? null : $"template:{template.Id}", cancellationToken) || template is null)
        {
            return null;
        }

        return new TemplateSummary(Key(template), template.Name, template.Description, BuiltIn: false, template.CreatedAt);
    }

    /// <summary>
    /// Supprime définitivement un modèle enregistré. Les formulaires créés à partir de lui, leurs brouillons et
    /// leurs réponses ne changent pas. Les modèles livrés ne se suppriment pas.
    /// </summary>
    public async Task<LifecycleStatus> DeleteAsync(CurrentUser user, Guid templateId, CancellationToken cancellationToken)
    {
        var template = await store.FindTemplateAsync(templateId, cancellationToken);
        if (!await AllowedAsync(user, template is null ? null : $"template:{template.Id}", cancellationToken))
        {
            return LifecycleStatus.Forbidden;
        }

        if (template is null)
        {
            return LifecycleStatus.NotFound;
        }

        store.RemoveTemplate(template);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.FormTemplateDeleted, user.Id, $"template:{template.Id}", cancellationToken);
        return LifecycleStatus.Ok;
    }

    /// <summary>
    /// Fichier du modèle avec la nouvelle adresse et le nouveau titre ; le modèle PDF éventuel reçoit un nom de
    /// fichier fictif, seul son contenu compte. Null si le fichier n'est pas un objet JSON.
    /// </summary>
    public static string? Rename(string packageJson, string slug, string title)
    {
        JsonObject? package;
        try
        {
            package = JsonNode.Parse(packageJson) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (package is null)
        {
            return null;
        }

        package["slug"] = slug;
        package["title"] = title;
        if (package.ContainsKey("pdfTemplate"))
        {
            package["pdfTemplate"] = $"{slug}.pdf.html";
        }

        return package.ToJsonString();
    }

    private async Task<bool> AllowedAsync(CurrentUser user, string? target, CancellationToken cancellationToken)
    {
        if (user.IsAdmin)
        {
            return true;
        }

        await audit.RecordAsync(AuditActions.AccessDenied, user.Id, target, DeniedDetail, cancellationToken);
        return false;
    }

    private async Task<ResolvedTemplate?> FindAsync(string? key, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(key, out var id))
        {
            var saved = await store.FindTemplateAsync(id, cancellationToken);
            return saved is null
                ? null
                : new ResolvedTemplate(Key(saved), saved.Name, saved.Description, false, saved.PackageJson, saved.PdfTemplateHtml, $"template:{saved.Id}");
        }

        var builtIn = BuiltInFormTemplates.Find(key);
        return builtIn is null
            ? null
            : new ResolvedTemplate(builtIn.Code, builtIn.Name, builtIn.Description, true, builtIn.PackageJson, null, builtIn.Code);
    }

    private static string Key(FormTemplate template) => template.Id.ToString("D");

    /// <param name="AuditName">Nom dans le journal : code d'un modèle livré, <c>template:{id}</c> pour un modèle enregistré.</param>
    private sealed record ResolvedTemplate(string Key, string Name, string? Description, bool BuiltIn, string PackageJson, string? PdfTemplateHtml, string AuditName)
    {
        /// <summary>Cible d'un refus journalisé : seulement un modèle enregistré (les modèles livrés n'ont pas d'identifiant).</summary>
        public string? AuditTarget => BuiltIn ? null : AuditName;
    }
}

public interface IFormTemplateStore
{
    Task<IReadOnlyList<FormTemplate>> ListTemplatesAsync(CancellationToken cancellationToken);

    Task<FormTemplate?> FindTemplateAsync(Guid templateId, CancellationToken cancellationToken);

    /// <summary>Formulaire avec ses versions ; null s'il n'existe pas.</summary>
    Task<Form?> FindFormAsync(Guid formId, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    void AddForm(Form form);

    void AddDraft(FormDraft draft);

    void AddTemplate(FormTemplate template);

    void RemoveTemplate(FormTemplate template);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <param name="Key">Code d'un modèle livré ou identifiant d'un modèle enregistré.</param>
/// <param name="CreatedAt">Date d'enregistrement ; null pour un modèle livré.</param>
public sealed record TemplateSummary(string Key, string Name, string? Description, bool BuiltIn, DateTimeOffset? CreatedAt);

public sealed record TemplateSection(string? Title, int FieldCount);

/// <param name="Errors">Erreurs du fichier du modèle : non vides, le modèle ne peut pas servir.</param>
public sealed record TemplateDetail(
    string Key,
    string Name,
    string? Description,
    bool BuiltIn,
    string? DefaultSlug,
    string? DefaultTitle,
    AccessMode? AccessMode,
    bool HasPdfTemplate,
    bool HasReceipt,
    IReadOnlyList<TemplateSection> Sections,
    IReadOnlyList<DefinitionError> Errors);

public abstract record TemplateUseResult
{
    /// <param name="NeedsAccessCode">Modèle en mode code d'accès : le code sera demandé à la publication.</param>
    public sealed record Created(Guid FormId, string Slug, bool NeedsAccessCode) : TemplateUseResult;

    public sealed record Invalid(IReadOnlyList<DefinitionError> Errors) : TemplateUseResult;

    public sealed record NotFound : TemplateUseResult;

    public sealed record Forbidden : TemplateUseResult;
}

public abstract record TemplateSaveResult
{
    public sealed record Saved(Guid TemplateId) : TemplateSaveResult;

    public sealed record Invalid(IReadOnlyList<DefinitionError> Errors) : TemplateSaveResult;

    public sealed record NotFound : TemplateSaveResult;

    public sealed record Forbidden : TemplateSaveResult;
}
