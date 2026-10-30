using CseForms.Domain.Forms;

namespace CseForms.Application.PublicForms;

public interface IPublicFormStore
{
    /// <summary>Formulaire publié ou fermé, avec sa version courante ; null s'il n'existe pas ou est en brouillon.</summary>
    Task<PublishedForm?> FindPublishedAsync(string slug, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> GetActiveSiteLabelsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enregistre la réponse dans une transaction qui verrouille le formulaire : contrôle d'ouverture,
    /// du nombre maximal de réponses et de la version remplie, puis attribution du numéro de dossier.
    /// </summary>
    Task<SaveSubmissionOutcome> SaveSubmissionAsync(SaveSubmissionCommand command, CancellationToken cancellationToken);
}

public sealed record PublishedForm(Form Form, FormVersion Version, int ResponseCount);

public sealed record SaveSubmissionCommand(Guid FormId, Guid FilledVersionId, string DataJson, DateTimeOffset Now, int CaseYear);

public abstract record SaveSubmissionOutcome
{
    public sealed record Created(Guid SubmissionId, string CaseNumber) : SaveSubmissionOutcome;

    public sealed record NotAccepting : SaveSubmissionOutcome;

    public sealed record VersionChanged : SaveSubmissionOutcome;
}
