using CseForms.Domain.Common;
using CseForms.Domain.Settings;
using CseForms.Domain.Sites;

namespace CseForms.Domain.Tests;

public class ReferenceDataTests
{
    [Fact]
    public void Site_is_active_by_default_and_can_be_deactivated()
    {
        var site = new Site("  Riom ");

        Assert.Equal("Riom", site.Label);
        Assert.True(site.IsActive);

        site.Deactivate();
        Assert.False(site.IsActive);
    }

    [Fact]
    public void Site_label_is_required() =>
        Assert.Throws<DomainException>(() => new Site(" "));

    [Fact]
    public void Setting_key_is_required() =>
        Assert.Throws<DomainException>(() => new Setting("", "valeur"));
}
