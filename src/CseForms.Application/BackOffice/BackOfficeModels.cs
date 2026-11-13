using System.Text.Json.Nodes;
using CseForms.Domain.Submissions;

namespace CseForms.Application.BackOffice;

public static class AppRoles
{
    public const string Admin = "admin";
    public const string Editor = "editor";
    public const string Reader = "reader";

    public static readonly IReadOnlyList<string> All = [Admin, Editor, Reader];
}

public static class AuditActions
{
    public const string LoginSucceeded = "login.success";
    public const string LoginFailed = "login.failure";
    public const string LoginLockedOut = "login.lockout";
    public const string LoginTwoFactorFailed = "login.2fa.failure";
    public const string LoginRecoveryCodeUsed = "login.2fa.recovery_code";
    public const string TwoFactorEnabled = "2fa.enable";
    public const string TwoFactorDisabled = "2fa.disable";
    public const string TwoFactorRecoveryCodesRenewed = "2fa.recovery_codes";
    public const string TwoFactorReset = "2fa.reset";
    public const string Logout = "logout";
    public const string SubmissionViewed = "submission.view";
    public const string AccessDenied = "access.denied";
    public const string SubmissionDownloaded = "submission.download";
    public const string PdfRegenerationRequested = "submission.pdf.regenerate";
    public const string QrCodeDownloaded = "form.qrcode";
    public const string FlyerGenerated = "form.flyer";
    public const string FlyerCodeRejected = "form.flyer.code_rejected";
    public const string SubmissionStatusChanged = "submission.status";
    public const string SubmissionDeleted = "submission.delete";
    public const string SubmissionPurged = "submission.purge";
    public const string SubmissionsExported = "form.export";
    public const string FormClosed = "form.close";
    public const string FormReopened = "form.reopen";
    public const string FormScheduleChanged = "form.schedule";
    public const string FormAccessCodeChanged = "form.access_code";
    public const string FormImported = "form.import";
    public const string FormMadePublic = "form.public";
    public const string FormNotificationsChanged = "form.notifications";
    public const string FormNotificationsRemoved = "form.notifications.remove";
    public const string SettingsChanged = "settings.change";
    public const string SiteAdded = "site.add";
    public const string SiteDisabled = "site.disable";
    public const string SiteEnabled = "site.enable";
    public const string PasswordChanged = "password.change";
    public const string UserCreated = "user.create";
    public const string UserUpdated = "user.update";
    public const string UserDisabled = "user.disable";
    public const string UserEnabled = "user.enable";
    public const string UserPasswordReset = "user.password_reset";
    public const string BootstrapAdminCreated = "user.bootstrap";
}

/// <summary>Utilisateur connecté, tel que vu par les cas d'usage. Un lecteur ne déclenche aucune action.</summary>
public sealed record CurrentUser(Guid Id, bool IsAdmin, bool IsReadOnly = false);

public sealed record FormSummary(Guid Id, string Slug, string Title, string Status, int ResponseCount);

public sealed record FormHeader(Guid Id, string Slug, string Title, string? CurrentSchemaJson);

public sealed record SubmissionFilter(
    string? Search = null,
    string? Status = null,
    string? Site = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool OldestFirst = false,
    int Page = 1)
{
    public const int PageSize = 25;
}

/// <summary>Critères résolus pour le stockage : bornes en UTC, champ site de la définition courante.</summary>
public sealed record SubmissionQuery(
    Guid FormId,
    string? CaseNumberContains,
    string? Status,
    string? SiteFieldId,
    string? Site,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtcExclusive,
    bool OldestFirst,
    int Page);

/// <summary>Ligne de la liste : aucune donnée saisie hormis le site, utile au tri des dossiers.</summary>
public sealed record SubmissionListItem(Guid Id, string CaseNumber, DateTimeOffset CreatedAt, string Status, string? Site);

public sealed record SubmissionPage(IReadOnlyList<SubmissionListItem> Items, int TotalCount, int Page, int PageCount, IReadOnlyList<string> Statuses);

public sealed record SubmissionRecord(
    Guid Id,
    Guid FormId,
    string FormTitle,
    string CaseNumber,
    int VersionNumber,
    string SchemaJson,
    string DataJson,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PurgeAt,
    int? RetentionDays = null);

public sealed record SubmissionDetail(
    Guid Id,
    Guid FormId,
    string FormTitle,
    string CaseNumber,
    int VersionNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PurgeAt,
    IReadOnlyList<PresentedSection> Sections,
    PdfState Pdf,
    IReadOnlyList<HistoryEntry> History,
    int? RetentionDays);

/// <summary>
/// État du PDF d'une réponse : date du PDF disponible, génération en cours (première ou régénération),
/// échec définitif de la dernière génération.
/// </summary>
public sealed record PdfState(DateTimeOffset? GeneratedAt, bool Generating, bool Failed)
{
    public bool Available => GeneratedAt is not null;
}

/// <summary>PDF stocké, à déchiffrer pour le téléchargement.</summary>
public sealed record StoredPdf(Guid FileId, string StoragePath);

public sealed record PdfDownload(string FileName, byte[] Content);

public enum PdfRequestOutcome
{
    Requested,
    AlreadyPending,
    NotFound,
    Forbidden,
}

public sealed record PresentedSection(string? Title, IReadOnlyList<PresentedItem> Items);

public abstract record PresentedItem(string Label);

public sealed record PresentedValue(string Label, string Value) : PresentedItem(Label);

public sealed record PresentedTable(string Label, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) : PresentedItem(Label);

/// <summary>
/// Statuts de traitement, communs à tous les formulaires pour l'instant. Quitter « Nouvelle »
/// fait courir la durée de conservation du formulaire (voir <see cref="Submission.ChangeStatus"/>).
/// </summary>
public static class SubmissionStatuses
{
    public const string New = Submission.InitialStatus;
    public const string Entered = "entered";
    public const string Archived = "archived";

    public static readonly IReadOnlyList<string> All = [New, Entered, Archived];

    public static bool IsKnown(string? status) => status is not null && All.Contains(status);

    public static string Label(string status) => status switch
    {
        New => "Nouvelle",
        Entered => "Saisie dans le logiciel CSE",
        Archived => "Archivée",
        _ => status,
    };
}

public enum StatusChangeOutcome
{
    Changed,
    Unchanged,
    Invalid,
    NotFound,
    Forbidden,
}

public enum DeletionOutcome
{
    Deleted,

    /// <summary>Le numéro de dossier saisi pour confirmer ne correspond pas.</summary>
    NotConfirmed,
    NotFound,
    Forbidden,
}

public sealed record DeletionResult(DeletionOutcome Outcome, Guid FormId = default);

/// <summary>Réponse à confirmer avant suppression : numéro de dossier et formulaire, sans donnée saisie.</summary>
public sealed record DeletionScreen(Guid Id, Guid FormId, string FormTitle, string CaseNumber);

/// <summary>Ligne brute d'historique : action d'audit (avec l'auteur) ou tâche de fond terminée.</summary>
public sealed record HistoryRecord(DateTimeOffset At, string Action, string? Detail, string? Actor);

/// <summary>Ligne affichée sur la fiche : date, libellé de l'action, auteur (absent pour une action automatique).</summary>
public sealed record HistoryEntry(DateTimeOffset At, string Label, string? Actor);

/// <summary>Réponse à exporter : version remplie et données, interprétées avec la définition de cette version.</summary>
public sealed record ExportRow(string CaseNumber, DateTimeOffset CreatedAt, string Status, int VersionNumber, string DataJson);

public sealed record ExportData(IReadOnlyList<ExportRow> Rows, IReadOnlyDictionary<int, string> SchemasByVersion);

public sealed record ExportFile(string FileName, byte[] Content, int RowCount);

public enum ExportOutcome
{
    Ok,
    NotFound,
    Forbidden,
}

public sealed record ExportResult(ExportOutcome Outcome, ExportFile? File = null);

internal static class JsonData
{
    public static JsonObject ParseObject(string json) => JsonNode.Parse(json) as JsonObject ?? [];
}
