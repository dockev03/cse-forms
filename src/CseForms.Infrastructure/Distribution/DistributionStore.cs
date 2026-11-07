using CseForms.Application.Distribution;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Distribution;

internal sealed class DistributionStore(CseFormsDbContext db) : IDistributionStore
{
    public async Task<DistributionForm?> GetFormAsync(Guid formId, CancellationToken cancellationToken)
    {
        var row = await (
            from f in db.Forms.AsNoTracking()
            where f.Id == formId
            select new
            {
                f.Id,
                f.Slug,
                f.Title,
                f.Status,
                f.AccessCodeHash,
                Schema = db.FormVersions.Where(v => v.Id == f.CurrentVersionId).Select(v => v.SchemaJson).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new DistributionForm(row.Id, row.Slug, row.Title, row.Status.ToString(), row.Schema, row.AccessCodeHash);
    }

    public async Task<IReadOnlyList<string>> GetActiveSiteLabelsAsync(CancellationToken cancellationToken) =>
        await db.Sites.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Label).Select(s => s.Label).ToListAsync(cancellationToken);
}
