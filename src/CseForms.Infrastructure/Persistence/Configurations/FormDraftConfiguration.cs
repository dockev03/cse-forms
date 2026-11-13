using CseForms.Domain.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CseForms.Infrastructure.Persistence.Configurations;

internal sealed class FormDraftConfiguration : IEntityTypeConfiguration<FormDraft>
{
    public void Configure(EntityTypeBuilder<FormDraft> builder)
    {
        builder.ToTable("form_draft");

        // Un brouillon par formulaire.
        builder.HasKey(d => d.FormId);
        builder.Property(d => d.FormId).ValueGeneratedNever();
        builder.HasOne<Form>().WithOne().HasForeignKey<FormDraft>(d => d.FormId).OnDelete(DeleteBehavior.Cascade);

        // Texte brut et non jsonb : le fichier est relu tel qu'importé, à l'aperçu comme à la publication.
        builder.Property(d => d.PackageJson).IsRequired();
        builder.Property(d => d.PdfTemplateHtml);
        builder.Property(d => d.SavedBy).HasMaxLength(256);
    }
}
