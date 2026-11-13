using CseForms.Application.Settings;
using CseForms.Domain.Settings;
using CseForms.Domain.Sites;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Settings;

internal sealed class SettingsAdminStore(CseFormsDbContext db) : ISettingsAdminStore
{
    public Task<Setting?> FindAsync(string key, CancellationToken cancellationToken) =>
        db.Settings.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);

    public void Add(Setting setting) => db.Settings.Add(setting);

    public void Remove(Setting setting) => db.Settings.Remove(setting);

    public async Task<IReadOnlyList<SiteView>> ListSitesAsync(CancellationToken cancellationToken) =>
        await db.Sites
            .AsNoTracking()
            .OrderBy(s => s.Label)
            .Select(s => new SiteView(s.Id, s.Label, s.IsActive))
            .ToListAsync(cancellationToken);

    public Task<Site?> FindSiteAsync(Guid id, CancellationToken cancellationToken) =>
        db.Sites.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<bool> SiteLabelExistsAsync(string label, CancellationToken cancellationToken)
    {
        var lowered = label.ToLowerInvariant();
        return db.Sites.AnyAsync(s => s.Label.ToLower() == lowered, cancellationToken);
    }

    public void AddSite(Site site) => db.Sites.Add(site);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
