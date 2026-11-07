using CseForms.Domain.Forms;
using CseForms.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CseForms.Infrastructure.Persistence.Configurations;

internal sealed class NotificationRuleConfiguration : IEntityTypeConfiguration<NotificationRule>
{
    public void Configure(EntityTypeBuilder<NotificationRule> builder)
    {
        builder.ToTable("notification_rule");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // Une règle par formulaire.
        builder.HasIndex(r => r.FormId).IsUnique();
        builder.HasOne<Form>().WithMany().HasForeignKey(r => r.FormId).OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(r => r.Recipients);
        builder.Property<List<string>>("_recipients").HasColumnName("recipients").IsRequired();
        builder.Property(r => r.Subject).HasMaxLength(NotificationRule.SubjectMaxLength);
        builder.Property(r => r.Body).HasMaxLength(NotificationRule.BodyMaxLength);
    }
}
