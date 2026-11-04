using CseForms.Domain.Jobs;
using CseForms.Domain.Submissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CseForms.Infrastructure.Persistence.Configurations;

internal sealed class SubmissionJobConfiguration : IEntityTypeConfiguration<SubmissionJob>
{
    public void Configure(EntityTypeBuilder<SubmissionJob> builder)
    {
        builder.ToTable("job");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(j => j.LastError).HasMaxLength(SubmissionJob.ErrorMaxLength);
        builder.Ignore(j => j.IsFinished);

        // Relève des tâches en attente par le Worker.
        builder.HasIndex(j => new { j.Status, j.CreatedAt });

        builder.HasOne<Submission>()
            .WithMany()
            .HasForeignKey(j => j.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
