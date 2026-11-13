using CseForms.Application.Notifications;
using CseForms.Domain.Notifications;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Notifications;

internal sealed class NotificationRuleStore(CseFormsDbContext db) : INotificationRuleStore
{
    public Task<NotificationForm?> GetFormAsync(Guid formId, CancellationToken cancellationToken) =>
        db.Forms
            .AsNoTracking()
            .Where(f => f.Id == formId)
            .Select(f => new NotificationForm(f.Id, f.Slug, f.Title, f.CaseNumberPrefix))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<NotificationRule?> FindRuleAsync(Guid formId, CancellationToken cancellationToken) =>
        db.NotificationRules.SingleOrDefaultAsync(r => r.FormId == formId, cancellationToken);

    public void Add(NotificationRule rule) => db.NotificationRules.Add(rule);

    public void Remove(NotificationRule rule) => db.NotificationRules.Remove(rule);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
