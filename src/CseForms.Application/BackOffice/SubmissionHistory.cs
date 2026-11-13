namespace CseForms.Application.BackOffice;

/// <summary>
/// Historique affiché sur la fiche d'une réponse : réception, actions des élus (journal d'audit)
/// et tâches de fond terminées. Les consultations restent dans le journal d'audit sans être affichées ici.
/// </summary>
public static class SubmissionHistory
{
    /// <summary>Préfixe des lignes issues des tâches de fond : <c>job.{type}.{statut}</c>, en minuscules.</summary>
    public const string JobPrefix = "job.";

    public static IReadOnlyList<HistoryEntry> Build(DateTimeOffset receivedAt, IEnumerable<HistoryRecord> records) =>
        records
            .Select(r => new HistoryEntry(r.At, Describe(r.Action, r.Detail), r.Actor))
            .Append(new HistoryEntry(receivedAt, "Réponse reçue", null))
            .OrderByDescending(e => e.At)
            .ToList();

    public static string Describe(string action, string? detail) => action switch
    {
        AuditActions.SubmissionStatusChanged => DescribeStatusChange(detail),
        AuditActions.SubmissionDownloaded => "PDF téléchargé",
        AuditActions.PdfRegenerationRequested => "Nouvelle génération du PDF demandée",
        AuditActions.AccessDenied => "Accès refusé",
        "job.pdf.succeeded" => "PDF généré",
        "job.pdf.failed" => "Échec de la génération du PDF",
        "job.notification.succeeded" => "Notification envoyée au CSE",
        "job.notification.failed" => "Échec de l'envoi de la notification",
        "job.receipt.succeeded" => "Accusé de réception envoyé",
        "job.receipt.failed" => "Échec de l'envoi de l'accusé de réception",
        _ => action,
    };

    /// <summary>Détail au format <c>ancien&gt;nouveau</c>, codes de statut uniquement.</summary>
    public static string StatusChangeDetail(string previous, string current) => $"{previous}>{current}";

    private static string DescribeStatusChange(string? detail)
    {
        var parts = (detail ?? string.Empty).Split('>', 2);
        return parts.Length == 2
            ? $"Statut changé : « {SubmissionStatuses.Label(parts[0])} » vers « {SubmissionStatuses.Label(parts[1])} »"
            : "Statut changé";
    }
}
