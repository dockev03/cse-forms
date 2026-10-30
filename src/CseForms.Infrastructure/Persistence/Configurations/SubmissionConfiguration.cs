using CseForms.Domain.Forms;
using CseForms.Domain.Submissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CseForms.Infrastructure.Persistence.Configurations;

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.ToTable("submission");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.CaseNumber).HasMaxLength(Submission.CaseNumberMaxLength).IsRequired();
        builder.HasIndex(s => s.CaseNumber).IsUnique();

        builder.Property(s => s.DataJson).HasColumnType("jsonb").IsRequired();
        builder.Property(s => s.Status).HasMaxLength(Submission.StatusMaxLength).IsRequired();

        // Une version référencée par des réponses ne peut pas être supprimée.
        builder.HasOne<FormVersion>()
            .WithMany()
            .HasForeignKey(s => s.FormVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.PurgeAt);
    }
}
