using System.Text.Json.Nodes;
using CseForms.Application.BackOffice;
using CseForms.Application.FormDefinitions;
using CseForms.Domain.Common;
using CseForms.Domain.Forms;
using CseForms.Domain.Notifications;

namespace CseForms.Application.FormAdministration;

/// <summary>
/// Duplication d'un formulaire, par exemple l'arbre de Noël d'une année sur l'autre. La version en ligne de la
/// source devient le brouillon d'un nouveau formulaire, hors ligne, à vérifier dans l'aperçu puis à publier.
/// Sont aussi recopiés la règle de notification, le filigrane et les comptes attribués. Ne sont jamais copiés :
/// les réponses, l'historique, le code d'accès (stocké haché, demandé à la publication), les dates d'ouverture et
/// de fermeture et le nombre maximal de réponses, propres à chaque édition. Réservé aux administrateurs, comme
/// la création d'un formulaire ; tout est enregistré en une fois.
/// </summary>
public sealed class FormDuplicationService(
    IFormDuplicationStore store,
    IAuditLog audit,
    TimeProvider timeProvider)
{
    public const string NotPublished = "Ce formulaire n'a pas de version en ligne : seule une version publiée peut être dupliquée.";

    public const string SlugTaken = "Cette adresse est déjà utilisée par un autre formulaire.";

    /// <summary>Formulaire à dupliquer, pour l'écran de duplication ; null s'il n'existe pas ou si l'accès est refusé (journalisé).</summary>
    public async Task<DuplicationSource?> GetSourceAsync(CurrentUser user, Guid sourceFormId, CancellationToken cancellationToken)
    {
        var source = await store.FindFormAsync(sourceFormId, cancellationToken);
        if (!user.IsAdmin)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, source is null ? null : Target(source.Id), "form-duplicate", cancellationToken);
            return null;
        }

        return source is null
            ? null
            : new DuplicationSource(source.Id, source.Slug, source.Title, source.CurrentVersion?.Number, source.AccessCodeHash is not null);
    }

    public async Task<DuplicationResult> DuplicateAsync(
        CurrentUser user,
        Guid sourceFormId,
        string? slug,
        string? title,
        CancellationToken cancellationToken)
    {
        var source = await store.FindFormAsync(sourceFormId, cancellationToken);
        if (!user.IsAdmin)
        {
            await audit.RecordAsync(
                AuditActions.AccessDenied,
                user.Id,
                source is null ? null : Target(source.Id),
                "form-duplicate",
                cancellationToken);
            return new DuplicationResult.Forbidden();
        }

        if (source is null)
        {
            return new DuplicationResult.NotFound();
        }

        if (source.CurrentVersion is not { } version)
        {
            return Invalid("source", NotPublished);
        }

        slug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        title = (title ?? string.Empty).Trim();
        var now = timeProvider.GetUtcNow();
        Form copy;
        try
        {
            copy = new Form(slug, title, now);
        }
        catch (DomainException ex)
        {
            return Invalid(ex.Message.StartsWith("Le slug", StringComparison.Ordinal) ? "slug" : "title", ex.Message);
        }

        if (await store.SlugExistsAsync(copy.Slug, cancellationToken))
        {
            return Invalid("slug", SlugTaken);
        }

        // Fichier de formulaire reconstruit puis relu : la copie passe les mêmes contrôles qu'un import.
        var packageJson = BuildPackage(source, version, copy.Slug, copy.Title);
        var read = FormImportService.ReadUpload(packageJson, version.PdfTemplateHtml);
        if (read.Errors.Count > 0)
        {
            return new DuplicationResult.Invalid(read.Errors);
        }

        var savedBy = $"user:{user.Id}";
        copy.SetPdfWatermark(source.PdfWatermark);
        store.AddForm(copy);
        store.AddDraft(new FormDraft(copy.Id, packageJson, version.PdfTemplateHtml, now, savedBy));

        if (await store.FindNotificationRuleAsync(source.Id, cancellationToken) is { } rule)
        {
            store.AddNotificationRule(new NotificationRule(copy.Id, rule.Recipients, rule.Subject, rule.Body));
        }

        var grantedAccounts = await store.CopyFormAccessAsync(source.Id, copy.Id, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            AuditActions.FormDuplicated,
            user.Id,
            Target(copy.Id),
            $"from={Target(source.Id)};v{version.Number};access={grantedAccounts}",
            cancellationToken);
        return new DuplicationResult.Created(copy.Id, copy.Slug, version.Number, grantedAccounts, source.AccessCodeHash is not null);
    }

    /// <summary>
    /// Fichier de formulaire équivalent à la version en ligne de la source, avec la nouvelle adresse et le
    /// nouveau titre. Le modèle PDF reçoit un nom de fichier fictif : seul son contenu, joint au brouillon, compte.
    /// </summary>
    public static string BuildPackage(Form source, FormVersion version, string slug, string title)
    {
        var package = new JsonObject
        {
            ["slug"] = slug,
            ["title"] = title,
            ["caseNumberPrefix"] = source.CaseNumberPrefix,
        };

        if (source.RetentionDays is { } days)
        {
            package["retentionDays"] = days;
        }

        package["access"] = new JsonObject { ["mode"] = source.AccessCodeHash is null ? "public" : "pin" };

        if (version.UiJson is not null)
        {
            package["ui"] = JsonNode.Parse(version.UiJson);
        }

        if (version.PdfTemplateHtml is not null)
        {
            package["pdfTemplate"] = $"{slug}.pdf.html";
        }

        foreach (var (key, value) in JsonNode.Parse(version.SchemaJson)!.AsObject())
        {
            if (key != "title")
            {
                package[key] = value?.DeepClone();
            }
        }

        return package.ToJsonString();
    }

    private static DuplicationResult.Invalid Invalid(string path, string message) => new([new DefinitionError(path, message)]);

    private static string Target(Guid formId) => $"form:{formId}";
}

public interface IFormDuplicationStore
{
    /// <summary>Formulaire avec ses versions ; null s'il n'existe pas.</summary>
    Task<Form?> FindFormAsync(Guid formId, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    Task<NotificationRule?> FindNotificationRuleAsync(Guid formId, CancellationToken cancellationToken);

    /// <summary>Attribue au formulaire cible les comptes attribués à la source (enregistré avec le reste) ; renvoie leur nombre.</summary>
    Task<int> CopyFormAccessAsync(Guid sourceFormId, Guid targetFormId, CancellationToken cancellationToken);

    void AddForm(Form form);

    void AddDraft(FormDraft draft);

    void AddNotificationRule(NotificationRule rule);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <param name="Version">Version en ligne ; null si le formulaire n'a jamais été publié (duplication impossible).</param>
public sealed record DuplicationSource(Guid FormId, string Slug, string Title, int? Version, bool Protected);

public abstract record DuplicationResult
{
    /// <param name="NeedsAccessCode">Source protégée par code : le code du nouveau formulaire sera demandé à sa publication.</param>
    public sealed record Created(Guid FormId, string Slug, int SourceVersion, int GrantedAccounts, bool NeedsAccessCode) : DuplicationResult;

    public sealed record Invalid(IReadOnlyList<DefinitionError> Errors) : DuplicationResult;

    public sealed record NotFound : DuplicationResult;

    public sealed record Forbidden : DuplicationResult;
}
