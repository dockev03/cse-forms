using CseForms.Domain.Common;
using CseForms.Domain.Notifications;

namespace CseForms.Domain.Tests;

public class NotificationRuleTests
{
    [Fact]
    public void Recipients_are_trimmed_lowercased_and_deduplicated()
    {
        var rule = new NotificationRule(Guid.NewGuid(), [" Elu@Example.test ", "elu@example.test", "", "autre@example.test"], null, null);

        Assert.Equal(["elu@example.test", "autre@example.test"], rule.Recipients);
        Assert.Null(rule.Subject);
        Assert.Null(rule.Body);
    }

    [Theory]
    [InlineData("pas-une-adresse")]
    [InlineData("Élu <elu@example.test>")]
    [InlineData("elu@example.test\r\nBcc: autre@example.test")]
    public void Invalid_recipients_are_refused(string recipient) =>
        Assert.Throws<DomainException>(() => new NotificationRule(Guid.NewGuid(), [recipient], null, null));

    [Fact]
    public void At_least_one_and_at_most_twenty_recipients()
    {
        Assert.Throws<DomainException>(() => new NotificationRule(Guid.NewGuid(), [], null, null));
        var many = Enumerable.Range(0, NotificationRule.MaxRecipients + 1).Select(i => $"elu{i}@example.test");
        Assert.Throws<DomainException>(() => new NotificationRule(Guid.NewGuid(), many, null, null));
    }

    [Fact]
    public void Subject_stays_on_one_line()
    {
        Assert.Throws<DomainException>(() => new NotificationRule(Guid.NewGuid(), ["elu@example.test"], "Objet\nBcc: x@example.test", null));

        var rule = new NotificationRule(Guid.NewGuid(), ["elu@example.test"], "  Dossier {numero}  ", "Ligne 1\r\nLigne 2\n");
        Assert.Equal("Dossier {numero}", rule.Subject);
        Assert.Equal("Ligne 1\nLigne 2", rule.Body);
    }

    [Fact]
    public void Too_long_texts_are_refused() =>
        Assert.Throws<DomainException>(() => new NotificationRule(Guid.NewGuid(), ["elu@example.test"], new string('x', NotificationRule.SubjectMaxLength + 1), null));
}
