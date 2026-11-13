using CseForms.Application.FormAdministration;
using CseForms.Domain.Forms;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.FormAdministration;

internal sealed class FormTemplateStore(CseFormsDbContext db) : IFormTemplateStore
{
    public async Task<IReadOnlyList<FormTemplate>> ListTemplatesAsync(CancellationToken cancellationToken) =>
        await db.FormTemplates.AsNoTracking().ToListAsync(cancellationToken);

    public Task<FormTemplate?> FindTemplateAsync(Guid templateId, CancellationToken cancellationToken) =>
        db.FormTemplates.SingleOrDefaultAsync(t => t.Id == templateId, cancellationToken);

    public Task<Form?> FindFormAsync(Guid formId, CancellationToken cancellationToken) =>
        db.Forms.AsNoTracking().Include(f => f.Versions).SingleOrDefaultAsync(f => f.Id == formId, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        db.Forms.AnyAsync(f => f.Slug == slug, cancellationToken);

    public void AddForm(Form form) => db.Forms.Add(form);

    public void AddDraft(FormDraft draft) => db.FormDrafts.Add(draft);

    public void AddTemplate(FormTemplate template) => db.FormTemplates.Add(template);

    public void RemoveTemplate(FormTemplate template) => db.FormTemplates.Remove(template);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
