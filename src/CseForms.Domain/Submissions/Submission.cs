using CseForms.Domain.Common;

namespace CseForms.Domain.Submissions;

/// <summary>
/// Réponse à un formulaire. Elle référence toujours la version remplie, pour que
/// les anciennes réponses et leur PDF restent lisibles après une nouvelle publication.
/// </summary>
public sealed class Submission
{
    public const int CaseNumberMaxLength = 32;
    public const int StatusMaxLength = 64;
    public const string InitialStatus = "new";

    private Submission()
    {
        CaseNumber = null!;
        DataJson = null!;
        Status = null!;
    }

    internal Submission(
        Guid formVersionId,
        string caseNumber,
        string dataJson,
        DateTimeOffset createdAt,
        DateTimeOffset? purgeAt)
    {
        if (string.IsNullOrWhiteSpace(caseNumber) || caseNumber.Length > CaseNumberMaxLength)
        {
            throw new DomainException("Numéro de dossier invalide.");
        }

        if (string.IsNullOrWhiteSpace(dataJson))
        {
            throw new DomainException("Les données de la réponse sont obligatoires.");
        }

        Id = Guid.CreateVersion7();
        FormVersionId = formVersionId;
        CaseNumber = caseNumber;
        DataJson = dataJson;
        Status = InitialStatus;
        CreatedAt = Utc.Require(createdAt, nameof(createdAt));
        PurgeAt = purgeAt;
    }

    public Guid Id { get; private set; }

    /// <summary>Numéro lisible, par exemple <c>INS-2026-0042</c>.</summary>
    public string CaseNumber { get; private set; }

    public Guid FormVersionId { get; private set; }

    /// <summary>Données validées et normalisées, stockées en JSONB.</summary>
    public string DataJson { get; private set; }

    /// <summary>Code du statut de traitement ; les statuts sont paramétrables par formulaire.</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PurgeAt { get; private set; }

    public void ChangeStatus(string status)
    {
        var value = (status ?? string.Empty).Trim();

        if (value.Length == 0 || value.Length > StatusMaxLength)
        {
            throw new DomainException("Statut invalide.");
        }

        Status = value;
    }

    public void SchedulePurge(DateTimeOffset? purgeAt)
    {
        if (purgeAt is { } p)
        {
            Utc.Require(p, nameof(purgeAt));
        }

        PurgeAt = purgeAt;
    }
}
