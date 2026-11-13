using CseForms.Application.BackOffice;
using CseForms.Domain.Audit;
using CseForms.Infrastructure.Persistence;

namespace CseForms.Infrastructure.BackOffice;

internal sealed class AuditLog(CseFormsDbContext db, TimeProvider timeProvider) : IAuditLog
{
    public async Task RecordAsync(string action, Guid? userId, string? target, string? detail, CancellationToken cancellationToken)
    {
        db.AuditLog.Add(new AuditEntry(userId, action, target, timeProvider.GetUtcNow(), detail));
        await db.SaveChangesAsync(cancellationToken);
    }
}
