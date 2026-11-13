using CseForms.Application.FormAdministration;
using CseForms.Domain.Forms;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.FormAdministration;

internal sealed class FormDraftStore(CseFormsDbContext db) : IFormDraftStore
{
    public Task<Form?> FindFormAsync(Guid formId, CancellationToken cancellationToken) =>
        db.Forms.Include(f => f.Versions).SingleOrDefaultAsync(f => f.Id == formId, cancellationToken);

    public Task<Form?> FindFormBySlugAsync(string slug, CancellationToken cancellationToken) =>
        db.Forms.Include(f => f.Versions).SingleOrDefaultAsync(f => f.Slug == slug, cancellationToken);

    public Task<FormDraft?> FindDraftAsync(Guid formId, CancellationToken cancellationToken) =>
        db.FormDrafts.SingleOrDefaultAsync(d => d.FormId == formId, cancellationToken);

    public async Task<IReadOnlyCollection<string>> GetActiveSiteLabelsAsync(CancellationToken cancellationToken) =>
        await db.Sites.AsNoTracking().Where(s => s.IsActive).Select(s => s.Label).ToListAsync(cancellationToken);

    public void AddForm(Form form) => db.Forms.Add(form);

    public void AddDraft(FormDraft draft) => db.FormDrafts.Add(draft);

    public void RemoveDraft(FormDraft draft) => db.FormDrafts.Remove(draft);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
