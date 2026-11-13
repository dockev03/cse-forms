using System.Globalization;
using CseForms.Application.BackOffice;
using CseForms.Application.FormDefinitions;
using CseForms.Domain.Forms;

namespace CseForms.Application.FormAdministration;

/// <summary>
/// Import d'un fichier de formulaire depuis le back-office, avec les mêmes contrôles que <c>forms import</c>.
/// Créer un formulaire est réservé aux administrateurs ; un éditeur ne publie une nouvelle version que d'un
/// formulaire qui lui est attribué. Un éditeur sans droit reçoit le même message, que le formulaire existe ou
/// non : son existence n'est pas révélée. Passer un formulaire protégé en accès public demande une confirmation.
/// </summary>
public sealed class FormImportService(
    IBackOfficeStore backOffice,
    IFormAdminStore store,
    FormAdminService admin,
    IAuditLog audit)
{
    /// <summary>Taille maximale de chaque fichier envoyé (définition, modèle PDF).</summary>
    public const int MaxFileBytes = 256 * 1024;

    public const string NotAllowed =
        "Ce formulaire n'existe pas ou ne vous est pas attribué. Seul un administrateur peut créer un formulaire.";

    public async Task<FormImportResult> ImportAsync(CurrentUser user, FormImportRequest request, CancellationToken cancellationToken)
    {
        if (user.IsReadOnly)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, null, "form-import", cancellationToken);
            return new FormImportResult.Forbidden();
        }

        var read = FormPackageReader.Read(request.DefinitionJson, _ => request.PdfTemplateHtml);
        if (!read.IsValid)
        {
            return new FormImportResult.Invalid(read.Errors);
        }

        var package = read.Package!;
        if (request.PdfTemplateHtml is not null && package.PdfTemplateHtml is null)
        {
            return Invalid("$.pdfTemplate", "Un modèle PDF a été envoyé, mais le fichier de formulaire ne déclare pas de modèle (pdfTemplate).");
        }

        var existing = await store.FindBySlugAsync(package.Slug.ToLowerInvariant(), cancellationToken);
        if (existing is null ? !user.IsAdmin : !await backOffice.CanAccessFormAsync(user, existing.Id, cancellationToken))
        {
            await audit.RecordAsync(
                AuditActions.AccessDenied,
                user.Id,
                existing is null ? null : $"form:{existing.Id}",
                "form-import",
                cancellationToken);
            return new FormImportResult.Invalid([new DefinitionError("$.slug", NotAllowed)]);
        }

        var accessCode = request.AccessCode?.Trim();
        if (!string.IsNullOrEmpty(accessCode) && !string.Equals(accessCode, request.AccessCodeConfirmation?.Trim(), StringComparison.Ordinal))
        {
            return Invalid("code", "Les deux saisies du code d'accès sont différentes.");
        }

        var becomesPublic = existing?.AccessCodeHash is not null && package.AccessMode == AccessMode.Public;
        if (becomesPublic && !request.ConfirmPublic)
        {
            return new FormImportResult.ConfirmPublicRequired();
        }

        // Le journal d'audit partage le contexte de base : rien n'est journalisé sur un import refusé par
        // FormAdminService, qui a pu modifier le formulaire suivi sans l'enregistrer.
        var previousRetention = existing?.RetentionDays;
        var outcome = await admin.ImportAsync(package, accessCode, $"user:{user.Id}", cancellationToken);
        switch (outcome)
        {
            case ImportOutcome.AccessCodeRequired:
                return new FormImportResult.AccessCodeRequired();
            case ImportOutcome.Invalid invalid:
                return Invalid(string.IsNullOrEmpty(accessCode) ? "$" : "code", invalid.Message);
        }

        var form = existing ?? await store.FindBySlugAsync(package.Slug.ToLowerInvariant(), cancellationToken);
        var target = $"form:{form!.Id}";
        if (outcome is ImportOutcome.Published published)
        {
            var detail = (published.Created ? "create;" : string.Empty) + "v" + published.Version.ToString(CultureInfo.InvariantCulture);
            await audit.RecordAsync(AuditActions.FormImported, user.Id, target, detail, cancellationToken);
        }

        if (!string.IsNullOrEmpty(accessCode))
        {
            await audit.RecordAsync(AuditActions.FormAccessCodeChanged, user.Id, target, cancellationToken);
        }

        if (becomesPublic)
        {
            await audit.RecordAsync(AuditActions.FormMadePublic, user.Id, target, cancellationToken);
        }

        return new FormImportResult.Done(
            form.Id,
            form.Slug,
            outcome switch
            {
                ImportOutcome.Published p => p.Version,
                ImportOutcome.Unchanged u => u.Version,
                _ => 0,
            },
            Created: outcome is ImportOutcome.Published { Created: true },
            Unchanged: outcome is ImportOutcome.Unchanged,
            RetentionChange: existing is not null && previousRetention != package.RetentionDays
                ? new RetentionChange(previousRetention, package.RetentionDays)
                : null,
            BecamePublic: becomesPublic,
            StillClosed: form.Status == FormStatus.Closed);
    }

    private static FormImportResult.Invalid Invalid(string path, string message) => new([new DefinitionError(path, message)]);
}

/// <param name="PdfTemplateHtml">Modèle PDF envoyé avec la définition, utilisé quel que soit le nom déclaré.</param>
/// <param name="AccessCode">Code d'accès à définir (obligatoire au premier import d'un formulaire en mode pin).</param>
/// <param name="ConfirmPublic">Confirmation explicite du passage d'un formulaire protégé en accès public.</param>
public sealed record FormImportRequest(
    string DefinitionJson,
    string? PdfTemplateHtml = null,
    string? AccessCode = null,
    string? AccessCodeConfirmation = null,
    bool ConfirmPublic = false);

public sealed record RetentionChange(int? Before, int? After);

public abstract record FormImportResult
{
    public sealed record Done(
        Guid FormId,
        string Slug,
        int Version,
        bool Created,
        bool Unchanged,
        RetentionChange? RetentionChange,
        bool BecamePublic,
        bool StillClosed) : FormImportResult;

    public sealed record Invalid(IReadOnlyList<DefinitionError> Errors) : FormImportResult;

    /// <summary>Premier import d'un formulaire en mode pin : le code d'accès doit être saisi.</summary>
    public sealed record AccessCodeRequired : FormImportResult;

    /// <summary>Le fichier passe un formulaire protégé en accès public : à confirmer.</summary>
    public sealed record ConfirmPublicRequired : FormImportResult;

    public sealed record Forbidden : FormImportResult;
}
