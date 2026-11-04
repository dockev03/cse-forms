using CseForms.Domain.Common;

namespace CseForms.Domain.Submissions;

public enum SubmissionFileKind
{
    Pdf,
}

/// <summary>
/// Fichier rattaché à une réponse (PDF généré). Le contenu est stocké chiffré hors de la racine web ;
/// le chemin est relatif au dossier de stockage et l'empreinte porte sur le contenu en clair.
/// </summary>
public sealed class SubmissionFile
{
    public const int StoragePathMaxLength = 256;

    private SubmissionFile()
    {
        StoragePath = null!;
        Sha256 = null!;
    }

    public SubmissionFile(Guid id, Guid submissionId, SubmissionFileKind kind, string storagePath, string sha256, long sizeBytes, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || storagePath.Length > StoragePathMaxLength)
        {
            throw new DomainException("Chemin de stockage invalide.");
        }

        if (sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit))
        {
            throw new DomainException("Empreinte SHA-256 invalide.");
        }

        Id = id;
        SubmissionId = submissionId;
        Kind = kind;
        StoragePath = storagePath;
        Sha256 = sha256.ToLowerInvariant();
        SizeBytes = sizeBytes;
        CreatedAt = Utc.Require(createdAt, nameof(createdAt));
    }

    public Guid Id { get; private set; }

    public Guid SubmissionId { get; private set; }

    public SubmissionFileKind Kind { get; private set; }

    public string StoragePath { get; private set; }

    public string Sha256 { get; private set; }

    public long SizeBytes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
