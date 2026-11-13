using CseForms.Application.ProofOfWork;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CseForms.Infrastructure.ProofOfWork;

/// <summary>Défi de preuve de travail déjà utilisé : empêche de rejouer une solution. Aucune donnée personnelle.</summary>
internal sealed class UsedProofOfWork
{
    public required string Challenge { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}

internal sealed class UsedProofOfWorkConfiguration : IEntityTypeConfiguration<UsedProofOfWork>
{
    public void Configure(EntityTypeBuilder<UsedProofOfWork> builder)
    {
        builder.ToTable("proof_of_work_used");
        builder.HasKey(p => p.Challenge);
        builder.Property(p => p.Challenge).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(p => p.ExpiresAt);
    }
}

internal sealed class ProofOfWorkStore(CseFormsDbContext db) : IProofOfWorkStore
{
    public async Task<bool> TryConsumeAsync(string challenge, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO proof_of_work_used (challenge, expires_at)
            VALUES ({challenge}, {expiresAt})
            ON CONFLICT (challenge) DO NOTHING
            """,
            cancellationToken) == 1;

    public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<UsedProofOfWork>().Where(p => p.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
}
