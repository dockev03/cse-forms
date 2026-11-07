using CseForms.Domain.Common;

namespace CseForms.Domain.Jobs;

public enum JobType
{
    Pdf,

    /// <summary>Notification au CSE, sans donnée personnelle.</summary>
    Notification,

    /// <summary>Accusé de réception au répondant, sans donnée saisie.</summary>
    Receipt,
}

public enum JobStatus
{
    /// <summary>Enregistrée avec la réponse, pas encore confiée à la file de tâches.</summary>
    Pending,

    /// <summary>Confiée à la file de tâches, éventuellement en attente d'un nouvel essai.</summary>
    Queued,
    Succeeded,

    /// <summary>Échec définitif après le nombre maximal d'essais.</summary>
    Failed,
}

/// <summary>
/// Tâche de fond liée à une réponse (table <c>job</c>). Elle est enregistrée dans la même transaction
/// que la réponse : aucune tâche n'est perdue si le serveur s'arrête juste après la soumission.
/// </summary>
public sealed class SubmissionJob
{
    public const int MaxAttempts = 3;
    public const int ErrorMaxLength = 200;

    private SubmissionJob()
    {
    }

    public SubmissionJob(Guid submissionId, JobType type, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        SubmissionId = submissionId;
        Type = type;
        Status = JobStatus.Pending;
        CreatedAt = Utc.Require(createdAt, nameof(createdAt));
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public Guid SubmissionId { get; private set; }

    public JobType Type { get; private set; }

    public JobStatus Status { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>Cause technique du dernier échec (type d'erreur), jamais une donnée saisie.</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsFinished => Status is JobStatus.Succeeded or JobStatus.Failed;

    public void MarkQueued(DateTimeOffset now)
    {
        if (Status != JobStatus.Pending)
        {
            throw new DomainException("Seule une tâche en attente peut être confiée à la file.");
        }

        Status = JobStatus.Queued;
        UpdatedAt = Utc.Require(now, nameof(now));
    }

    public void MarkSucceeded(DateTimeOffset now)
    {
        Attempts++;
        Status = JobStatus.Succeeded;
        LastError = null;
        UpdatedAt = Utc.Require(now, nameof(now));
    }

    /// <summary>Enregistre un essai manqué ; renvoie vrai s'il reste des essais.</summary>
    public bool RecordFailure(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = string.IsNullOrWhiteSpace(error) ? "Erreur" : error.Trim()[..Math.Min(error.Trim().Length, ErrorMaxLength)];
        UpdatedAt = Utc.Require(now, nameof(now));

        if (Attempts >= MaxAttempts)
        {
            Status = JobStatus.Failed;
            return false;
        }

        return true;
    }
}
