using CseForms.Domain.Jobs;
using CseForms.Domain.Submissions;

namespace CseForms.Application.Pdf;

/// <summary>Conversion HTML vers PDF (Gotenberg). Le HTML ne charge aucune ressource externe.</summary>
public interface IPdfConverter
{
    Task<byte[]> ConvertAsync(PdfHtml html, CancellationToken cancellationToken);
}

/// <summary>Corps du document et pied de page répété (numéro de dossier et de page).</summary>
public sealed record PdfHtml(string Body, string Footer);

/// <summary>
/// Stockage chiffré des fichiers, hors de la racine web. Toute altération du fichier
/// est détectée à la lecture.
/// </summary>
public interface IFileVault
{
    Task<StoredFile> WriteAsync(Guid fileId, Guid submissionId, byte[] content, CancellationToken cancellationToken);

    Task<byte[]> ReadAsync(string storagePath, Guid fileId, CancellationToken cancellationToken);

    void Delete(string storagePath);
}

/// <summary>Fichier écrit : chemin relatif au dossier de stockage, empreinte et taille du contenu en clair.</summary>
public sealed record StoredFile(string StoragePath, string Sha256, long SizeBytes);

public interface ISubmissionPdfStore
{
    /// <summary>Tâche suivie pour modification ; null si elle n'existe plus (réponse supprimée).</summary>
    Task<SubmissionJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken);

    Task<PdfSource?> GetSourceAsync(Guid submissionId, CancellationToken cancellationToken);

    /// <summary>Ajoute le nouveau PDF et retire le précédent ; renvoie les chemins des fichiers à effacer après enregistrement.</summary>
    Task<IReadOnlyList<string>> ReplacePdfAsync(SubmissionFile file, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Abandonne l'ajout et le retrait de fichiers non enregistrés ; la tâche reste suivie.</summary>
    void DiscardFileChanges();
}

/// <summary>
/// Tout ce que le PDF affiche : la réponse, la version remplie (définition et modèle PDF éventuel)
/// et les paramètres du CSE.
/// </summary>
public sealed record PdfSource(
    Guid SubmissionId,
    string FormTitle,
    string CaseNumber,
    int VersionNumber,
    DateTimeOffset SubmittedAt,
    string SchemaJson,
    string DataJson,
    IReadOnlyDictionary<string, string> Settings,
    string? PdfTemplateHtml = null);
