using CseForms.Domain.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CseForms.Infrastructure.Persistence.Configurations;

internal sealed class FormVersionConfiguration : IEntityTypeConfiguration<FormVersion>
{
    public void Configure(EntityTypeBuilder<FormVersion> builder)
    {
        builder.ToTable("form_version");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.HasIndex(v => new { v.FormId, v.Number }).IsUnique();

        builder.Property(v => v.SchemaJson).HasColumnType("jsonb").IsRequired();
        builder.Property(v => v.UiJson).HasColumnType("jsonb");
        builder.Property(v => v.PdfTemplateHtml);
        builder.Property(v => v.CreatedBy).HasMaxLength(256);
    }
}
