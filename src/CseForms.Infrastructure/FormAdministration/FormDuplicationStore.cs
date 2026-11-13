using CseForms.Application.FormAdministration;
using CseForms.Domain.Forms;
using CseForms.Domain.Notifications;
using CseForms.Infrastructure.Identity;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.FormAdministration;

internal sealed class FormDuplicationStore(CseFormsDbContext db) : IFormDuplicationStore
{
    public Task<Form?> FindFormAsync(Guid formId, CancellationToken cancellationToken) =>
        db.Forms.AsNoTracking().Include(f => f.Versions).SingleOrDefaultAsync(f => f.Id == formId, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        db.Forms.AnyAsync(f => f.Slug == slug, cancellationToken);

    public Task<NotificationRule?> FindNotificationRuleAsync(Guid formId, CancellationToken cancellationToken) =>
        db.NotificationRules.AsNoTracking().SingleOrDefaultAsync(r => r.FormId == formId, cancellationToken);

    public async Task<int> CopyFormAccessAsync(Guid sourceFormId, Guid targetFormId, CancellationToken cancellationToken)
    {
        var userIds = await db.UserFormAccess
            .Where(a => a.FormId == sourceFormId)
            .Select(a => a.UserId)
            .ToListAsync(cancellationToken);
        db.UserFormAccess.AddRange(userIds.Select(userId => new UserFormAccess { UserId = userId, FormId = targetFormId }));
        return userIds.Count;
    }

    public void AddForm(Form form) => db.Forms.Add(form);

    public void AddDraft(FormDraft draft) => db.FormDrafts.Add(draft);

    public void AddNotificationRule(NotificationRule rule) => db.NotificationRules.Add(rule);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
