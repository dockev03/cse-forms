using CseForms.Application.BackOffice;
using CseForms.Application.FormDefinitions;
using CseForms.Domain.Common;
using CseForms.Domain.Forms;

namespace CseForms.Application.FormAdministration;

/// <summary>
/// Brouillon d'un formulaire : fichier importé sans être publié, vérifié dans l'aperçu (mobile et ordinateur),
/// puis publié ou supprimé. Mêmes droits que l'import : créer un formulaire est réservé aux administrateurs, un
/// éditeur n'agit que sur un formulaire qui lui est attribué, un lecteur ne voit pas les brouillons. Le
/// brouillon n'est jamais servi au public. Sa publication passe par l'import, avec les mêmes contrôles
/// (code d'accès, passage en public, version inchangée) et le même journal.
/// </summary>
public sealed class FormDraftService(
    IBackOfficeStore backOffice,
    IFormDraftStore store,
    FormImportService import,
    IAuditLog audit,
    TimeProvider timeProvider)
{
    /// <summary>Enregistre (ou remplace) le brouillon du formulaire désigné par le <c>slug</c> du fichier.</summary>
    public Task<DraftSaveResult> SaveAsync(CurrentUser user, string definitionJson, string? pdfTemplateHtml, CancellationToken cancellationToken) =>
        SaveAsync(user, definitionJson, pdfTemplateHtml, fromEditor: false, cancellationToken);

    /// <param name="fromEditor">Brouillon produit par l'éditeur visuel, signalé dans le journal (<c>editor</c>).</param>
    public async Task<DraftSaveResult> SaveAsync(
        CurrentUser user,
        string definitionJson,
        string? pdfTemplateHtml,
        bool fromEditor,
        CancellationToken cancellationToken)
    {
        if (user.IsReadOnly)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, null, "form-draft", cancellationToken);
            return new DraftSaveResult.Forbidden();
        }

        var read = FormImportService.ReadUpload(definitionJson, pdfTemplateHtml);
        if (read.Errors.Count > 0)
        {
            return new DraftSaveResult.Invalid(read.Errors);
        }

        var package = read.Package!;
        var form = await store.FindFormBySlugAsync(package.Slug.ToLowerInvariant(), cancellationToken);
        if (form is null ? !user.IsAdmin : !await backOffice.CanAccessFormAsync(user, form.Id, cancellationToken))
        {
            await audit.RecordAsync(
                AuditActions.AccessDenied,
                user.Id,
                form is null ? null : Target(form.Id),
                "form-draft",
                cancellationToken);
            return new DraftSaveResult.Invalid([new DefinitionError("$.slug", FormImportService.NotAllowed)]);
        }

        var now = timeProvider.GetUtcNow();
        var savedBy = $"user:{user.Id}";
        var created = form is null;
        try
        {
            if (form is null)
            {
                // Formulaire nouveau : créé en brouillon, sans version ; il reste hors ligne jusqu'à la publication.
                form = new Form(package.Slug, package.Title, now);
                store.AddForm(form);
            }

            var draft = await store.FindDraftAsync(form.Id, cancellationToken);
            if (draft is null)
            {
                store.AddDraft(new FormDraft(form.Id, definitionJson, package.PdfTemplateHtml, now, savedBy));
            }
            else
            {
                draft.Replace(definitionJson, package.PdfTemplateHtml, now, savedBy);
            }
        }
        catch (DomainException ex)
        {
            return new DraftSaveResult.Invalid([new DefinitionError("$", ex.Message)]);
        }

        await store.SaveChangesAsync(cancellationToken);
        var detail = (created, fromEditor) switch
        {
            (true, true) => "create;editor",
            (true, false) => "create",
            (false, true) => "editor",
            _ => null,
        };
        await audit.RecordAsync(AuditActions.FormDraftSaved, user.Id, Target(form.Id), detail, cancellationToken);
        return new DraftSaveResult.Saved(form.Id, form.Slug, created);
    }

    /// <summary>Ce que l'aperçu affiche ; <see cref="LifecycleResult{T}.Value"/> est null si le formulaire n'a pas de brouillon.</summary>
    public async Task<LifecycleResult<DraftPreview>> GetPreviewAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var (status, form) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != LifecycleStatus.Ok)
        {
            return new(status);
        }

        var draft = await store.FindDraftAsync(formId, cancellationToken);
        if (draft is null)
        {
            return new(LifecycleStatus.Ok);
        }

        // Relu à chaque fois : le brouillon est contrôlé avec les règles en vigueur, comme il le sera à la publication.
        var read = FormImportService.ReadUpload(draft.PackageJson, draft.PdfTemplateHtml);
        var package = read.Package;
        var sites = package is null ? [] : await store.GetActiveSiteLabelsAsync(cancellationToken);

        return new(LifecycleStatus.Ok, new DraftPreview(
            form!.Id,
            form.Slug,
            package?.Title ?? form.Title,
            form.Status,
            form.CurrentVersion?.Number,
            draft.SavedAt,
            package?.SchemaJson,
            package?.UiJson,
            RequiresAccessCode: package?.AccessMode == AccessMode.Pin,
            NeedsAccessCode: package?.AccessMode == AccessMode.Pin && form.AccessCodeHash is null,
            MakesPublic: package?.AccessMode == AccessMode.Public && form.AccessCodeHash is not null,
            sites.Order(StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), ignoreCase: true)).ToArray(),
            read.Errors));
    }

    /// <summary>
    /// Publie le brouillon comme un import du même fichier. Le brouillon est supprimé une fois publié (ou s'il
    /// est identique à la version en ligne) ; il est conservé si la publication est refusée.
    /// </summary>
    public async Task<DraftPublishResult> PublishAsync(
        CurrentUser user,
        Guid formId,
        string? accessCode,
        string? accessCodeConfirmation,
        bool confirmPublic,
        CancellationToken cancellationToken)
    {
        var (status, _) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != LifecycleStatus.Ok)
        {
            return new DraftPublishResult(status, null);
        }

        var draft = await store.FindDraftAsync(formId, cancellationToken);
        if (draft is null)
        {
            return new DraftPublishResult(LifecycleStatus.NotFound, null);
        }

        var result = await import.ImportAsync(
            user,
            new FormImportRequest(draft.PackageJson, draft.PdfTemplateHtml, accessCode, accessCodeConfirmation, confirmPublic, FromDraft: true),
            cancellationToken);

        if (result is FormImportResult.Done)
        {
            store.RemoveDraft(draft);
            await store.SaveChangesAsync(cancellationToken);
        }

        return new DraftPublishResult(LifecycleStatus.Ok, result);
    }

    /// <summary>Supprime le brouillon ; les versions publiées et les réponses ne changent pas.</summary>
    public async Task<LifecycleResult> DeleteAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var (status, _) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != LifecycleStatus.Ok)
        {
            return new(status);
        }

        var draft = await store.FindDraftAsync(formId, cancellationToken);
        if (draft is not null)
        {
            store.RemoveDraft(draft);
            await store.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(AuditActions.FormDraftDeleted, user.Id, Target(formId), cancellationToken);
        }

        return LifecycleResult.Ok;
    }

    /// <summary>Mêmes règles que les réglages : 404 sans droit sur le formulaire, 403 pour un lecteur.</summary>
    private async Task<(LifecycleStatus Status, Form? Form)> AuthorizeAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var form = await store.FindFormAsync(formId, cancellationToken);
        if (form is null)
        {
            return (LifecycleStatus.NotFound, null);
        }

        if (!await backOffice.CanAccessFormAsync(user, formId, cancellationToken))
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, Target(formId), cancellationToken);
            return (LifecycleStatus.NotFound, null);
        }

        if (user.IsReadOnly)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, Target(formId), cancellationToken);
            return (LifecycleStatus.Forbidden, null);
        }

        return (LifecycleStatus.Ok, form);
    }

    private static string Target(Guid formId) => $"form:{formId}";
}

public interface IFormDraftStore
{
    /// <summary>Formulaire suivi, avec ses versions ; null s'il n'existe pas.</summary>
    Task<Form?> FindFormAsync(Guid formId, CancellationToken cancellationToken);

    Task<Form?> FindFormBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Brouillon suivi pour modification ; null si le formulaire n'en a pas.</summary>
    Task<FormDraft?> FindDraftAsync(Guid formId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> GetActiveSiteLabelsAsync(CancellationToken cancellationToken);

    void AddForm(Form form);

    void AddDraft(FormDraft draft);

    void RemoveDraft(FormDraft draft);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public abstract record DraftSaveResult
{
    public sealed record Saved(Guid FormId, string Slug, bool CreatedForm) : DraftSaveResult;

    public sealed record Invalid(IReadOnlyList<DefinitionError> Errors) : DraftSaveResult;

    public sealed record Forbidden : DraftSaveResult;
}

/// <param name="Import">Résultat de l'import du brouillon ; null si l'accès est refusé ou s'il n'y a pas de brouillon.</param>
public sealed record DraftPublishResult(LifecycleStatus Status, FormImportResult? Import);

/// <summary>
/// Aperçu d'un brouillon. <paramref name="DefinitionJson"/> est null et <paramref name="Errors"/> non vide si le
/// fichier ne passe plus les contrôles (règles durcies depuis son enregistrement) : il ne peut alors être ni
/// affiché ni publié.
/// </summary>
/// <param name="NeedsAccessCode">Formulaire protégé sans code défini : le code sera demandé à la publication.</param>
/// <param name="MakesPublic">Le brouillon rend public un formulaire protégé : confirmation demandée à la publication.</param>
public sealed record DraftPreview(
    Guid FormId,
    string Slug,
    string Title,
    FormStatus FormStatus,
    int? PublishedVersion,
    DateTimeOffset SavedAt,
    string? DefinitionJson,
    string? UiJson,
    bool RequiresAccessCode,
    bool NeedsAccessCode,
    bool MakesPublic,
    IReadOnlyList<string> Sites,
    IReadOnlyList<DefinitionError> Errors);
