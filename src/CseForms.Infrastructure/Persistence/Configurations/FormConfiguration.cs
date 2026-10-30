using CseForms.Domain.Forms;
using CseForms.Domain.Submissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CseForms.Infrastructure.Persistence.Configurations;

internal sealed class FormConfiguration : IEntityTypeConfiguration<Form>
{
    public void Configure(EntityTypeBuilder<Form> builder)
    {
        builder.ToTable("form");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.Slug).HasMaxLength(Form.SlugMaxLength).IsRequired();
        builder.HasIndex(f => f.Slug).IsUnique();

        builder.Property(f => f.Title).HasMaxLength(Form.TitleMaxLength).IsRequired();
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(f => f.AccessCodeHash).HasMaxLength(256);
        builder.Property(f => f.CaseNumberPrefix)
            .HasMaxLength(CaseNumber.PrefixMaxLength)
            .HasDefaultValue(Form.DefaultCaseNumberPrefix)
            .IsRequired();

        builder.HasMany(f => f.Versions)
            .WithOne()
            .HasForeignKey(v => v.FormId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(f => f.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Pas de clé étrangère sur current_version_id : elle formerait un cycle avec
        // form_version.form_id et empêcherait d'enregistrer la première publication.
        // Le domaine garantit que la version courante appartient au formulaire.
        builder.Property(f => f.CurrentVersionId);

        builder.Ignore(f => f.CurrentVersion);
    }
}
