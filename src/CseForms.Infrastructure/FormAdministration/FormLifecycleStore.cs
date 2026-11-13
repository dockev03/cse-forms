using CseForms.Application.FormAdministration;
using CseForms.Domain.Forms;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.FormAdministration;

/// <summary>
/// Les changements ne touchent que les colonnes modifiées : une soumission en cours, qui verrouille la ligne
/// du formulaire (<c>FOR UPDATE</c>), les voit avant ou après, jamais à moitié.
/// </summary>
internal sealed class FormLifecycleStore(CseFormsDbContext db) : IFormLifecycleStore
{
    public Task<Form?> FindAsync(Guid formId, CancellationToken cancellationToken) =>
        db.Forms.Include(f => f.Versions).SingleOrDefaultAsync(f => f.Id == formId, cancellationToken);

    public Task<int> CountResponsesAsync(Guid formId, CancellationToken cancellationToken) =>
        db.Submissions.CountAsync(
            s => db.FormVersions.Any(v => v.Id == s.FormVersionId && v.FormId == formId),
            cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
