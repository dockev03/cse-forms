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

    /// <summary>Nom affiché d'un compte (filigrane du PDF) ; null s'il n'existe pas.</summary>
    Task<string?> GetUserDisplayNameAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Ajoute une tâche de génération, sauf si une génération est déjà en cours ; renvoie vrai si elle est ajoutée.</summary>
    Task<bool> RequestPdfAsync(Guid submissionId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Change le statut d'une réponse et tient sa date de purge à jour (conservation du formulaire).
    /// Renvoie le statut précédent, ou null si la réponse est introuvable ou déjà à ce statut.
    /// </summary>
    Task<string?> ChangeStatusAsync(Guid submissionId, string status, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Actions journalisées sur la réponse (hors consultations) et tâches de fond terminées, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(Guid submissionId, CancellationToken cancellationToken);

    /// <summary>Toutes les réponses filtrées (sans pagination), avec les définitions des versions remplies.</summary>
    Task<ExportData> GetExportDataAsync(SubmissionQuery query, CancellationToken cancellationToken);
}

public interface IAuditLog
{
    /// <summary>
    /// Enregistre une action. <paramref name="detail"/> est une précision technique (statuts, numéro de dossier,
    /// nombre de lignes), jamais une donnée saisie.
    /// </summary>
    Task RecordAsync(string action, Guid? userId, string? target, string? detail, CancellationToken cancellationToken);

    Task RecordAsync(string action, Guid? userId, string? target, CancellationToken cancellationToken) =>
        RecordAsync(action, userId, target, null, cancellationToken);
}
