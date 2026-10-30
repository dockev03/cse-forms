using CseForms.Domain.Forms;
using CseForms.Domain.Settings;
using CseForms.Domain.Sites;
using CseForms.Domain.Submissions;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Persistence;

public sealed class CseFormsDbContext(DbContextOptions<CseFormsDbContext> options) : DbContext(options)
{
    public DbSet<Form> Forms => Set<Form>();

    public DbSet<FormVersion> FormVersions => Set<FormVersion>();

    public DbSet<Submission> Submissions => Set<Submission>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CseFormsDbContext).Assembly);
}
