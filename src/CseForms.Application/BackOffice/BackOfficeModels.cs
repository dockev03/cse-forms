using System.Text.Json.Nodes;

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
    public const string Logout = "logout";
    public const string SubmissionViewed = "submission.view";
    public const string AccessDenied = "access.denied";
}

/// <summary>Utilisateur connecté, tel que vu par les cas d'usage.</summary>
public sealed record CurrentUser(Guid Id, bool IsAdmin);

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
    DateTimeOffset? PurgeAt);

public sealed record SubmissionDetail(
    Guid Id,
    Guid FormId,
    string FormTitle,
    string CaseNumber,
    int VersionNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PurgeAt,
    IReadOnlyList<PresentedSection> Sections);

public sealed record PresentedSection(string? Title, IReadOnlyList<PresentedItem> Items);

public abstract record PresentedItem(string Label);

public sealed record PresentedValue(string Label, string Value) : PresentedItem(Label);

public sealed record PresentedTable(string Label, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) : PresentedItem(Label);

public static class SubmissionStatuses
{
    /// <summary>Libellé affiché d'un statut ; les statuts paramétrables par formulaire viendront avec leur gestion.</summary>
    public static string Label(string status) => status switch
    {
        "new" => "Nouvelle",
        "entered" => "Saisie dans le logiciel CSE",
        "archived" => "Archivée",
        _ => status,
    };
}

internal static class JsonData
{
    public static JsonObject ParseObject(string json) => JsonNode.Parse(json) as JsonObject ?? [];
}
