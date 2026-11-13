using CseForms.Application.Common;
using CseForms.Application.FormDefinitions;
using CseForms.Application.Pdf;
using CseForms.Application.Retention;

namespace CseForms.Application.BackOffice;

/// <summary>
/// Cas d'usage du back-office. Les droits sont vérifiés ici, à chaque appel, quel que soit l'écran :
/// un compte sans droit sur le formulaire obtient la même réponse qu'une ressource inexistante.
/// </summary>
public sealed class BackOfficeService(
    IBackOfficeStore store,
    IAuditLog audit,
    IFileVault vault,
    SubmissionEraser eraser,
    TimeProvider timeProvider)
{
    public Task<IReadOnlyList<FormSummary>> ListFormsAsync(CurrentUser user, CancellationToken cancellationToken) =>
        store.ListFormsAsync(user, cancellationToken);

    public async Task<(FormHeader Form, SubmissionPage Page)?> ListSubmissionsAsync(
        CurrentUser user,
        Guid formId,
        SubmissionFilter filter,
        CancellationToken cancellationToken)
    {
        var form = await store.GetFormAsync(formId, cancellationToken);
        if (form is null)
        {
            return null;
        }

        if (!await store.CanAccessFormAsync(user, formId, cancellationToken))
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, $"form:{formId}", cancellationToken);
            return null;
        }

        return (form, await store.ListSubmissionsAsync(ToQuery(form, filter), cancellationToken));
    }

    /// <summary>
    /// Export CSV des réponses filtrées (toutes les pages), réservé aux administrateurs et éditeurs :
    /// c'est une extraction en masse de données personnelles. Chaque export est journalisé avec son nombre de lignes.
    /// </summary>
    public async Task<ExportResult> ExportCsvAsync(CurrentUser user, Guid formId, SubmissionFilter filter, CancellationToken cancellationToken)
    {
        var form = await store.GetFormAsync(formId, cancellationToken);
        if (form is null)
        {
            return new ExportResult(ExportOutcome.NotFound);
        }

        if (!await store.CanAccessFormAsync(user, formId, cancellationToken))
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, $"form:{formId}", cancellationToken);
            return new ExportResult(ExportOutcome.NotFound);
        }

        if (user.IsReadOnly)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, $"form:{formId}", cancellationToken);
            return new ExportResult(ExportOutcome.Forbidden);
        }

        var data = await store.GetExportDataAsync(ToQuery(form, filter), cancellationToken);
        var content = SubmissionCsvExporter.Write(data);
        var stamp = CseCalendar.ToLocal(timeProvider.GetUtcNow()).ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);

        await audit.RecordAsync(
            AuditActions.SubmissionsExported,
            user.Id,
            $"form:{formId}",
            data.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            cancellationToken);

        return new ExportResult(ExportOutcome.Ok, new ExportFile($"{form.Slug}-reponses-{stamp}.csv", content, data.Rows.Count));
    }

    public async Task<SubmissionDetail?> GetSubmissionAsync(CurrentUser user, Guid submissionId, CancellationToken cancellationToken)
    {
        var record = await FindAccessibleAsync(user, submissionId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        await audit.RecordAsync(AuditActions.SubmissionViewed, user.Id, $"submission:{submissionId}", cancellationToken);

        var definition = FormDefinitionParser.Parse(record.SchemaJson);
        var sections = definition.IsValid
            ? SubmissionPresenter.Present(definition.Definition!, JsonData.ParseObject(record.DataJson))
            : [];

        return new SubmissionDetail(
            record.Id,
            record.FormId,
            record.FormTitle,
            record.CaseNumber,
            record.VersionNumber,
            record.Status,
            record.CreatedAt,
            record.PurgeAt,
            sections,
            await store.GetPdfStateAsync(submissionId, cancellationToken),
            SubmissionHistory.Build(record.CreatedAt, await store.GetHistoryAsync(submissionId, cancellationToken)),
            record.RetentionDays);
    }

    /// <summary>Change le statut de traitement (éditeur ou administrateur) ; le changement est journalisé.</summary>
    public async Task<StatusChangeOutcome> ChangeStatusAsync(CurrentUser user, Guid submissionId, string? status, CancellationToken cancellationToken)
    {
        var record = await FindAccessibleAsync(user, submissionId, cancellationToken);
        if (record is null)
        {
            return StatusChangeOutcome.NotFound;
        }

        if (user.IsReadOnly)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, $"submission:{submissionId}", cancellationToken);
            return StatusChangeOutcome.Forbidden;
        }

        var value = status?.Trim();
        if (!SubmissionStatuses.IsKnown(value))
        {
            return StatusChangeOutcome.Invalid;
        }

        var previous = await store.ChangeStatusAsync(submissionId, value!, timeProvider.GetUtcNow(), cancellationToken);
        if (previous is null)
        {
            return StatusChangeOutcome.Unchanged;
        }

        await audit.RecordAsync(
            AuditActions.SubmissionStatusChanged,
            user.Id,
            $"submission:{submissionId}",
            SubmissionHistory.StatusChangeDetail(previous, value!),
            cancellationToken);
        return StatusChangeOutcome.Changed;
    }

    /// <summary>
    /// Suppression définitive d'une réponse et de son PDF, réservée aux administrateurs, après saisie
    /// du numéro de dossier pour confirmer. La trace d'audit garde le numéro de dossier, sans donnée saisie.
    /// </summary>
    public async Task<DeletionResult> DeleteSubmissionAsync(CurrentUser user, Guid submissionId, string? confirmation, CancellationToken cancellationToken)
    {
        var record = await FindAccessibleAsync(user, submissionId, cancellationToken);
        if (record is null)
        {
            return new DeletionResult(DeletionOutcome.NotFound);
        }

        if (!user.IsAdmin)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, $"submission:{submissionId}", cancellationToken);
            return new DeletionResult(DeletionOutcome.Forbidden, record.FormId);
        }

        if (!string.Equals(confirmation?.Trim(), record.CaseNumber, StringComparison.OrdinalIgnoreCase))
        {
            return new DeletionResult(DeletionOutcome.NotConfirmed, record.FormId);
        }

        if (!await eraser.EraseAsync(submissionId, dueAt: null, cancellationToken))
        {
            return new DeletionResult(DeletionOutcome.NotFound);
        }

        await audit.RecordAsync(AuditActions.SubmissionDeleted, user.Id, $"submission:{submissionId}", record.CaseNumber, cancellationToken);
        return new DeletionResult(DeletionOutcome.Deleted, record.FormId);
    }

    /// <summary>
    /// Écran de confirmation de suppression, sans journaliser de consultation. Réservé aux administrateurs :
    /// un autre rôle ayant le droit sur le formulaire est refusé et le refus est journalisé.
    /// </summary>
    public async Task<(DeletionOutcome Outcome, DeletionScreen? Screen)> GetDeletionScreenAsync(
        CurrentUser user,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var record = await FindAccessibleAsync(user, submissionId, cancellationToken);
        if (record is null)
        {
            return (DeletionOutcome.NotFound, null);
        }

        if (!user.IsAdmin)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, $"submission:{submissionId}", cancellationToken);
            return (DeletionOutcome.Forbidden, null);
        }

        return (DeletionOutcome.NotConfirmed, new DeletionScreen(record.Id, record.FormId, record.FormTitle, record.CaseNumber));
    }

    /// <summary>PDF déchiffré, prêt à télécharger ; chaque téléchargement est journalisé.</summary>
    public async Task<PdfDownload?> DownloadPdfAsync(CurrentUser user, Guid submissionId, CancellationToken cancellationToken)
    {
        var record = await FindAccessibleAsync(user, submissionId, cancellationToken);
        if (record is null || await store.GetPdfFileAsync(submissionId, cancellationToken) is not { } file)
        {
            return null;
        }

        var content = await vault.ReadAsync(file.StoragePath, file.FileId, cancellationToken);
        await audit.RecordAsync(AuditActions.SubmissionDownloaded, user.Id, $"submission:{submissionId}", cancellationToken);
        return new PdfDownload($"{record.CaseNumber}.pdf", content);
    }

    /// <summary>Demande une nouvelle génération du PDF (éditeur ou administrateur).</summary>
    public async Task<PdfRequestOutcome> RegeneratePdfAsync(CurrentUser user, Guid submissionId, CancellationToken cancellationToken)
    {
        var record = await FindAccessibleAsync(user, submissionId, cancellationToken);
        if (record is null)
        {
            return PdfRequestOutcome.NotFound;
        }

        if (user.IsReadOnly)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, $"submission:{submissionId}", cancellationToken);
            return PdfRequestOutcome.Forbidden;
        }

        if (!await store.RequestPdfAsync(submissionId, timeProvider.GetUtcNow(), cancellationToken))
        {
            return PdfRequestOutcome.AlreadyPending;
        }

        await audit.RecordAsync(AuditActions.PdfRegenerationRequested, user.Id, $"submission:{submissionId}", cancellationToken);
        return PdfRequestOutcome.Requested;
    }

    /// <summary>Réponse existante sur un formulaire accessible au compte ; un refus est journalisé.</summary>
    private async Task<SubmissionRecord?> FindAccessibleAsync(CurrentUser user, Guid submissionId, CancellationToken cancellationToken)
    {
        var record = await store.GetSubmissionAsync(submissionId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (!await store.CanAccessFormAsync(user, record.FormId, cancellationToken))
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, $"submission:{submissionId}", cancellationToken);
            return null;
        }

        return record;
    }

    private static SubmissionQuery ToQuery(FormHeader form, SubmissionFilter filter) =>
        new(
            form.Id,
            string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(filter.Status) ? null : filter.Status.Trim(),
            SiteFieldId(form.CurrentSchemaJson),
            string.IsNullOrWhiteSpace(filter.Site) ? null : filter.Site.Trim(),
            filter.From is { } from ? CseCalendar.StartOfDayUtc(from) : null,
            filter.To is { } to ? CseCalendar.StartOfDayUtc(to.AddDays(1)) : null,
            filter.OldestFirst,
            Math.Max(1, filter.Page));

    /// <summary>Premier champ de type site de la définition courante : sert au filtre par site.</summary>
    private static string? SiteFieldId(string? schemaJson)
    {
        if (schemaJson is null)
        {
            return null;
        }

        var parsed = FormDefinitionParser.Parse(schemaJson);
        return parsed.IsValid ? parsed.Definition!.AllTopLevelFields.FirstOrDefault(f => f.Type == FieldType.Site)?.Id : null;
    }
}
