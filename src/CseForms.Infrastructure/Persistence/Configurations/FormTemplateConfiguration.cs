using CseForms.Domain.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CseForms.Infrastructure.Persistence.Configurations;

internal sealed class FormTemplateConfiguration : IEntityTypeConfiguration<FormTemplate>
{
    public void Configure(EntityTypeBuilder<FormTemplate> builder)
    {
        builder.ToTable("form_template");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).IsRequired().HasMaxLength(FormTemplate.NameMaxLength);
        builder.Property(t => t.Description).HasMaxLength(FormTemplate.DescriptionMaxLength);

        // Texte brut, comme le brouillon : le fichier est relu tel qu'enregistré à chaque utilisation.
        builder.Property(t => t.PackageJson).IsRequired();
        builder.Property(t => t.PdfTemplateHtml);
        builder.Property(t => t.CreatedBy).HasMaxLength(256);
    }
}
