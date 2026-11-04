namespace CseForms.Application.BackOffice;

public interface IBackOfficeStore
{
    /// <summary>Formulaires visibles : tous pour un administrateur, sinon ceux attribués au compte.</summary>
    Task<IReadOnlyList<FormSummary>> ListFormsAsync(CurrentUser user, CancellationToken cancellationToken);

    Task<bool> CanAccessFormAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken);

    Task<FormHeader?> GetFormAsync(Guid formId, CancellationToken cancellationToken);

    Task<SubmissionPage> ListSubmissionsAsync(SubmissionQuery query, CancellationToken cancellationToken);

    Task<SubmissionRecord?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken);

    Task<PdfState> GetPdfStateAsync(Guid submissionId, CancellationToken cancellationToken);

    Task<StoredPdf?> GetPdfFileAsync(Guid submissionId, CancellationToken cancellationToken);

    /// <summary>Ajoute une tâche de génération, sauf si une génération est déjà en cours ; renvoie vrai si elle est ajoutée.</summary>
    Task<bool> RequestPdfAsync(Guid submissionId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IAuditLog
{
    Task RecordAsync(string action, Guid? userId, string? target, CancellationToken cancellationToken);
}
