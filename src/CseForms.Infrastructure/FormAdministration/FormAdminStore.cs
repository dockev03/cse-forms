using CseForms.Application.FormAdministration;
using CseForms.Domain.Forms;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.FormAdministration;

internal sealed class FormAdminStore(CseFormsDbContext db) : IFormAdminStore
{
    public Task<Form?> FindBySlugAsync(string slug, CancellationToken cancellationToken) =>
        db.Forms.Include(f => f.Versions).SingleOrDefaultAsync(f => f.Slug == slug, cancellationToken);

    public void Add(Form form) => db.Forms.Add(form);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<FormListItem>> ListAsync(CancellationToken cancellationToken) =>
        await db.Forms
            .AsNoTracking()
            .OrderBy(f => f.Slug)
            .Select(f => new FormListItem(
                f.Slug,
                f.Title,
                f.Status,
                db.FormVersions.Where(v => v.Id == f.CurrentVersionId).Select(v => (int?)v.Number).SingleOrDefault(),
                f.CaseNumberPrefix,
                f.AccessCodeHash != null,
                f.RetentionDays))
            .ToListAsync(cancellationToken);
}
