namespace CseForms.Application.BackOffice;

public interface IBackOfficeStore
{
    /// <summary>Formulaires visibles : tous pour un administrateur, sinon ceux attribués au compte.</summary>
    Task<IReadOnlyList<FormSummary>> ListFormsAsync(CurrentUser user, CancellationToken cancellationToken);

    Task<bool> CanAccessFormAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken);

    Task<FormHeader?> GetFormAsync(Guid formId, CancellationToken cancellationToken);

    Task<SubmissionPage> ListSubmissionsAsync(SubmissionQuery query, CancellationToken cancellationToken);

    Task<SubmissionRecord?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken);
}

public interface IAuditLog
{
    Task RecordAsync(string action, Guid? userId, string? target, CancellationToken cancellationToken);
}
