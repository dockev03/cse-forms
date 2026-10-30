using CseForms.Application.Common;
using CseForms.Application.FormDefinitions;

namespace CseForms.Application.BackOffice;

/// <summary>
/// Cas d'usage du back-office. Les droits sont vérifiés ici, à chaque appel, quel que soit l'écran :
/// un compte sans droit sur le formulaire obtient la même réponse qu'une ressource inexistante.
/// </summary>
public sealed class BackOfficeService(IBackOfficeStore store, IAuditLog audit)
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

        var query = new SubmissionQuery(
            formId,
            string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(filter.Status) ? null : filter.Status.Trim(),
            SiteFieldId(form.CurrentSchemaJson),
            string.IsNullOrWhiteSpace(filter.Site) ? null : filter.Site.Trim(),
            filter.From is { } from ? CseCalendar.StartOfDayUtc(from) : null,
            filter.To is { } to ? CseCalendar.StartOfDayUtc(to.AddDays(1)) : null,
            filter.OldestFirst,
            Math.Max(1, filter.Page));

        return (form, await store.ListSubmissionsAsync(query, cancellationToken));
    }

    public async Task<SubmissionDetail?> GetSubmissionAsync(CurrentUser user, Guid submissionId, CancellationToken cancellationToken)
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
            sections);
    }

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
