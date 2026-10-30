using CseForms.Domain.Forms;
using CseForms.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Persistence.Configurations;

/// <summary>Tables Identity renommées selon le modèle de données (app_user, role...).</summary>
internal static class IdentityConfiguration
{
    public static void Apply(ModelBuilder builder)
    {
        builder.Entity<AppUser>(user =>
        {
            user.ToTable("app_user");
            user.Property(u => u.DisplayName).HasMaxLength(AppUser.DisplayNameMaxLength).IsRequired();
        });
        builder.Entity<AppRole>().ToTable("role");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_role");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claim");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_login");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_token");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claim");

        builder.Entity<UserFormAccess>(access =>
        {
            access.ToTable("user_form_access");
            access.HasKey(a => new { a.UserId, a.FormId });
            access.HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            access.HasOne<Form>().WithMany().HasForeignKey(a => a.FormId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
