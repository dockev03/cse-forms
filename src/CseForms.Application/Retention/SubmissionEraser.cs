using CseForms.Application.Pdf;
using Microsoft.Extensions.Logging;

namespace CseForms.Application.Retention;

/// <summary>Réponse dont la date de purge est passée ; le numéro de dossier sert à la trace d'audit.</summary>
public sealed record DueSubmission(Guid Id, string CaseNumber);

public interface ISubmissionEraseStore
{
    /// <summary>Réponses dont la date de purge est atteinte, les plus anciennes d'abord.</summary>
    Task<IReadOnlyList<DueSubmission>> ListDueAsync(DateTimeOffset now, int max, CancellationToken cancellationToken);

    /// <summary>
    /// Supprime la réponse, ses fichiers enregistrés et ses tâches, en une transaction. Avec <paramref name="dueAt"/>,
    /// la suppression n'a lieu que si la date de purge est toujours atteinte (statut inchangé entre-temps).
    /// Renvoie les chemins des fichiers chiffrés à effacer, ou null si rien n'a été supprimé.
    /// </summary>
    Task<IReadOnlyList<string>?> DeleteAsync(Guid submissionId, DateTimeOffset? dueAt, CancellationToken cancellationToken);
}

/// <summary>
/// Suppression définitive d'une réponse : lignes en base, puis fichiers chiffrés (PDF). Commune à la
/// suppression unitaire du back-office et à la purge automatique. Aucune donnée saisie n'est journalisée.
/// </summary>
public sealed partial class SubmissionEraser(ISubmissionEraseStore store, IFileVault vault, ILogger<SubmissionEraser> logger)
{
    public async Task<bool> EraseAsync(Guid submissionId, DateTimeOffset? dueAt, CancellationToken cancellationToken)
    {
        var paths = await store.DeleteAsync(submissionId, dueAt, cancellationToken);
        if (paths is null)
        {
            return false;
        }

        foreach (var path in paths)
        {
            try
            {
                vault.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // La ligne est déjà supprimée : le fichier, chiffré, reste illisible sans sa référence ; on le signale.
                LogFileNotDeleted(ex, submissionId);
            }
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fichier chiffré non effacé pour la réponse {SubmissionId}")]
    private partial void LogFileNotDeleted(Exception exception, Guid submissionId);
}
