using CseForms.Domain.Audit;
using CseForms.Domain.Forms;
using CseForms.Domain.Settings;
using CseForms.Domain.Sites;
using CseForms.Domain.Submissions;
using CseForms.Infrastructure.Identity;
using CseForms.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Persistence;

public sealed class CseFormsDbContext(DbContextOptions<CseFormsDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public DbSet<Form> Forms => Set<Form>();

    public DbSet<FormVersion> FormVersions => Set<FormVersion>();

    public DbSet<Submission> Submissions => Set<Submission>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    public DbSet<UserFormAccess> UserFormAccess => Set<UserFormAccess>();

    internal DbSet<CaseNumberCounter> CaseNumberCounters => Set<CaseNumberCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        IdentityConfiguration.Apply(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CseFormsDbContext).Assembly);
    }
}
