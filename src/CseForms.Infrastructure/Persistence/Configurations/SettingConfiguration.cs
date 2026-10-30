using CseForms.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CseForms.Infrastructure.Persistence.Configurations;

internal sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("setting");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(Setting.KeyMaxLength);
        builder.Property(s => s.Value).IsRequired();
    }
}
