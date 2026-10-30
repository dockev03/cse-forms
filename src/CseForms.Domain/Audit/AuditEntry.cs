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

    private AuditEntry()
    {
        Action = null!;
    }

    public AuditEntry(Guid? userId, string action, string? target, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(action) || action.Length > ActionMaxLength)
        {
            throw new DomainException("Action d'audit invalide.");
        }

        if (target is { Length: > TargetMaxLength })
        {
            throw new DomainException("Cible d'audit trop longue.");
        }

        Id = Guid.CreateVersion7();
        UserId = userId;
        Action = action;
        Target = target;
        OccurredAt = Utc.Require(occurredAt, nameof(occurredAt));
    }

    public Guid Id { get; private set; }

    public Guid? UserId { get; private set; }

    public string Action { get; private set; }

    public string? Target { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
