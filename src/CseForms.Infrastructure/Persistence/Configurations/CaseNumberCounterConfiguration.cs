using CseForms.Domain.Submissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CseForms.Infrastructure.Persistence.Configurations;

internal sealed class CaseNumberCounterConfiguration : IEntityTypeConfiguration<CaseNumberCounter>
{
    public void Configure(EntityTypeBuilder<CaseNumberCounter> builder)
    {
        builder.ToTable("case_number_counter");
        builder.HasKey(c => new { c.Prefix, c.Year });
        builder.Property(c => c.Prefix).HasMaxLength(CaseNumber.PrefixMaxLength);
    }
}
