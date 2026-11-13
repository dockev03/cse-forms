using CseForms.Domain.Common;

namespace CseForms.Domain.Audit;

/// <summary>
/// Trace d'une action sensible. La cible est un identifiant technique (<c>submission:{id}</c>),
/// jamais une donnée saisie ni un nom.
/// </summary>
public sealed class AuditEntry
{
    public const int ActionMaxLength = 64;
    public const int TargetMaxLength = 128;
    public const int DetailMaxLength = 128;

    private AuditEntry()
    {
        Action = null!;
    }

    public AuditEntry(Guid? userId, string action, string? target, DateTimeOffset occurredAt, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(action) || action.Length > ActionMaxLength)
        {
            throw new DomainException("Action d'audit invalide.");
        }

        if (target is { Length: > TargetMaxLength })
        {
            throw new DomainException("Cible d'audit trop longue.");
        }

        if (detail is { Length: > DetailMaxLength })
        {
            throw new DomainException("Détail d'audit trop long.");
        }

        Id = Guid.CreateVersion7();
        UserId = userId;
        Action = action;
        Target = target;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail;
        OccurredAt = Utc.Require(occurredAt, nameof(occurredAt));
    }

    public Guid Id { get; private set; }

    public Guid? UserId { get; private set; }

    public string Action { get; private set; }

    public string? Target { get; private set; }

    /// <summary>Précision technique : statuts avant et après, numéro de dossier supprimé, nombre de lignes exportées.</summary>
    public string? Detail { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
