using CseForms.Domain.Submissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CseForms.Infrastructure.Persistence.Configurations;

internal sealed class SubmissionFileConfiguration : IEntityTypeConfiguration<SubmissionFile>
{
    public void Configure(EntityTypeBuilder<SubmissionFile> builder)
    {
        builder.ToTable("submission_file");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(f => f.StoragePath).HasMaxLength(SubmissionFile.StoragePathMaxLength).IsRequired();
        builder.Property(f => f.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();

        // Un seul PDF par réponse : une régénération remplace le précédent.
        builder.HasIndex(f => new { f.SubmissionId, f.Kind }).IsUnique();

        // La suppression d'une réponse supprime ses lignes ; la purge supprime aussi les fichiers.
        builder.HasOne<Submission>()
            .WithMany()
            .HasForeignKey(f => f.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
