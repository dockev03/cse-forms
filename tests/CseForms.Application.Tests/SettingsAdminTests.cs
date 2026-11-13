using CseForms.Application.BackOffice;
using CseForms.Application.Notifications;
using CseForms.Application.Settings;
using CseForms.Domain.Settings;
using CseForms.Domain.Sites;

namespace CseForms.Application.Tests;

/// <summary>Paramètres et sites réglés depuis le back-office : droits, validation, journal sans valeur.</summary>
public class SettingsAdminTests
{
    private static readonly CurrentUser Admin = new(Guid.NewGuid(), IsAdmin: true);
    private static readonly CurrentUser Editor = new(Guid.NewGuid(), IsAdmin: false);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    private readonly FakeStore _store = new();
    private readonly FakeAudit _audit = new();

    private SettingsAdminService Service() => new(_store, _store, _audit);

    [Fact]
    public async Task Values_are_normalized_saved_and_only_changed_keys_are_audited()
    {
        _store.Settings[SettingKeys.CseName] = new Setting(SettingKeys.CseName, "CSE fictif");
        _store.Settings[SettingKeys.CsePhone] = new Setting(SettingKeys.CsePhone, "01 00 00 00 00");

        var result = await Service().SaveAsync(Admin, new Dictionary<string, string?>
        {
            [SettingKeys.CseName] = " CSE fictif ",
            [SettingKeys.CseOffice] = "Secrétaire : Prénom Fictif  \r\nTrésorier : Autre Fictif\r\n\r\n",
            [SettingKeys.PublicBaseUrl] = "https://formulaires.example.test/",
            [SettingKeys.AlertsEmail] = "alertes@example.test",
            [SettingKeys.CsePhone] = "  ",
        }, CancellationToken.None);

        Assert.Equal(SettingsStatus.Ok, result.Status);
        Assert.Equal("Secrétaire : Prénom Fictif\nTrésorier : Autre Fictif", _store.Settings[SettingKeys.CseOffice].Value);
        Assert.Equal("https://formulaires.example.test", _store.Settings[SettingKeys.PublicBaseUrl].Value);
        Assert.False(_store.Settings.ContainsKey(SettingKeys.CsePhone));
        Assert.Equal("CSE fictif", _store.Settings[SettingKeys.CseName].Value);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("settings.change", "settings", "cse.phone,cse.office,app.publicBaseUrl,alerts.email"), entry);
        Assert.DoesNotContain("@", entry.Detail);
    }

    [Fact]
    public async Task Nothing_changed_means_no_save_and_no_audit()
    {
        _store.Settings[SettingKeys.CseName] = new Setting(SettingKeys.CseName, "CSE fictif");

        await Service().SaveAsync(Admin, new Dictionary<string, string?> { [SettingKeys.CseName] = "CSE fictif", [SettingKeys.CseEmail] = "" }, CancellationToken.None);

        Assert.Equal(0, _store.Saves);
        Assert.Empty(_audit.Entries);
    }

    [Theory]
    [InlineData("app.publicBaseUrl", "http://formulaires.example.test", "HTTPS")]
    [InlineData("app.publicBaseUrl", "https://formulaires.example.test/f/inscription", "sans chemin")]
    [InlineData("cse.email", "pas-une-adresse", "invalide")]
    [InlineData("alerts.email", "Admin <admin@example.test>", "invalide")]
    [InlineData("cse.name", "CSE\nfictif", "seule ligne")]
    public async Task One_invalid_value_refuses_the_whole_form(string key, string value, string expected)
    {
        var result = await Service().SaveAsync(Admin, new Dictionary<string, string?>
        {
            [SettingKeys.CsePhone] = "01 00 00 00 00",
            [key] = value,
        }, CancellationToken.None);

        Assert.Equal(SettingsStatus.Invalid, result.Status);
        Assert.Contains(expected, result.Errors![key]);
        Assert.Empty(_store.Settings);
        Assert.Equal(0, _store.Saves);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Unknown_or_secret_keys_are_ignored()
    {
        await Service().SaveAsync(Admin, new Dictionary<string, string?> { ["smtp.password"] = "x", [SettingKeys.CseLogo] = "data:image/png;base64,AAAA" }, CancellationToken.None);

        Assert.Empty(_store.Settings);
    }

    [Fact]
    public async Task Too_light_color_is_refused_and_nothing_is_saved()
    {
        var result = await Service().SaveAsync(Admin, new Dictionary<string, string?> { [SettingKeys.ThemePrimaryColor] = "#FFCC00" }, CancellationToken.None);

        Assert.Equal(SettingsStatus.Invalid, result.Status);
        Assert.Contains("Contraste insuffisant", result.Errors![SettingKeys.ThemePrimaryColor]);
        Assert.Empty(_store.Settings);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Color_is_saved_normalized_and_audited_without_its_value()
    {
        var result = await Service().SaveAsync(Admin, new Dictionary<string, string?> { [SettingKeys.ThemePrimaryColor] = " #A3004C " }, CancellationToken.None);

        Assert.Equal(SettingsStatus.Ok, result.Status);
        Assert.Equal("#a3004c", _store.Settings[SettingKeys.ThemePrimaryColor].Value);
        Assert.Equal([("settings.change", "settings", "theme.primaryColor")], _audit.Entries);
    }

    [Fact]
    public async Task Background_is_checked_on_its_content_saved_and_removed()
    {
        var service = Service();

        var refused = await service.SaveBackgroundAsync(Admin, "<svg onload=\"alert(1)\"/>"u8.ToArray(), CancellationToken.None);
        Assert.Equal(SettingsStatus.Invalid, refused.Status);
        Assert.Contains("512 Ko", refused.Errors![SettingKeys.ThemeBackground]);
        Assert.Equal(SettingsStatus.Invalid, (await service.SaveBackgroundAsync(Admin, new byte[BackgroundImage.MaxBytes + 1], CancellationToken.None)).Status);
        Assert.Empty(_store.Settings);

        Assert.Equal(SettingsStatus.Ok, (await service.SaveBackgroundAsync(Admin, Png, CancellationToken.None)).Status);
        Assert.StartsWith("data:image/png;base64,", _store.Settings[SettingKeys.ThemeBackground].Value);
        var page = (await service.GetPageAsync(Admin, CancellationToken.None)).Value!;
        Assert.NotNull(page.Background);
        Assert.Null(page.Logo);

        Assert.Equal(SettingsStatus.Ok, (await service.RemoveBackgroundAsync(Admin, CancellationToken.None)).Status);
        Assert.Empty(_store.Settings);
        Assert.Equal(
            [("settings.change", "settings", "theme.background"), ("settings.change", "settings", "theme.background")],
            _audit.Entries);
    }

    [Fact]
    public async Task Logo_is_checked_on_its_content_saved_and_removed()
    {
        var service = Service();

        var refused = await service.SaveLogoAsync(Admin, "<svg onload=\"alert(1)\"/>"u8.ToArray(), CancellationToken.None);
        Assert.Equal(SettingsStatus.Invalid, refused.Status);
        Assert.Contains("PNG ou JPEG", refused.Errors![SettingKeys.CseLogo]);
        Assert.Equal(SettingsStatus.Invalid, (await service.SaveLogoAsync(Admin, new byte[CseLogo.MaxBytes + 1], CancellationToken.None)).Status);
        Assert.Empty(_store.Settings);

        Assert.Equal(SettingsStatus.Ok, (await service.SaveLogoAsync(Admin, Png, CancellationToken.None)).Status);
        Assert.StartsWith("data:image/png;base64,", _store.Settings[SettingKeys.CseLogo].Value);
        Assert.NotNull((await service.GetPageAsync(Admin, CancellationToken.None)).Value!.Logo);

        Assert.Equal(SettingsStatus.Ok, (await service.RemoveLogoAsync(Admin, CancellationToken.None)).Status);
        Assert.Empty(_store.Settings);
        Assert.Equal(
            [("settings.change", "settings", "cse.logo"), ("settings.change", "settings", "cse.logo")],
            _audit.Entries);
    }

    [Fact]
    public async Task Sites_are_added_once_whatever_the_case_then_disabled_and_enabled()
    {
        var service = Service();

        Assert.Equal(SettingsStatus.Ok, (await service.AddSiteAsync(Admin, "  Site fictif ", CancellationToken.None)).Status);
        var duplicate = await service.AddSiteAsync(Admin, "SITE FICTIF", CancellationToken.None);
        Assert.Equal(SettingsStatus.Invalid, duplicate.Status);
        Assert.Contains("existe déjà", duplicate.Errors![SettingsAdminService.SiteField]);
        Assert.Equal(SettingsStatus.Invalid, (await service.AddSiteAsync(Admin, "   ", CancellationToken.None)).Status);
        Assert.Equal(SettingsStatus.Invalid, (await service.AddSiteAsync(Admin, new string('x', Site.LabelMaxLength + 1), CancellationToken.None)).Status);

        var site = Assert.Single(_store.Sites);
        Assert.Equal("Site fictif", site.Label);

        await service.SetSiteActiveAsync(Admin, site.Id, active: false, CancellationToken.None);
        Assert.False(site.IsActive);
        await service.SetSiteActiveAsync(Admin, site.Id, active: false, CancellationToken.None);
        await service.SetSiteActiveAsync(Admin, site.Id, active: true, CancellationToken.None);
        Assert.True(site.IsActive);
        Assert.Equal(SettingsStatus.NotFound, (await service.SetSiteActiveAsync(Admin, Guid.NewGuid(), active: false, CancellationToken.None)).Status);

        var target = $"site:{site.Id}";
        Assert.Equal(
            [("site.add", target, null), ("site.disable", target, null), ("site.enable", target, null)],
            _audit.Entries);
    }

    [Fact]
    public async Task Non_administrators_are_refused_and_logged()
    {
        _store.Sites.Add(new Site("Site fictif"));
        var service = Service();

        Assert.Equal(SettingsStatus.Forbidden, (await service.GetPageAsync(Editor, CancellationToken.None)).Status);
        Assert.Equal(SettingsStatus.Forbidden, (await service.SaveAsync(Editor, new Dictionary<string, string?> { [SettingKeys.CseName] = "Pirate" }, CancellationToken.None)).Status);
        Assert.Equal(SettingsStatus.Forbidden, (await service.SaveLogoAsync(Editor, Png, CancellationToken.None)).Status);
        Assert.Equal(SettingsStatus.Forbidden, (await service.RemoveLogoAsync(Editor, CancellationToken.None)).Status);
        Assert.Equal(SettingsStatus.Forbidden, (await service.SaveBackgroundAsync(Editor, Png, CancellationToken.None)).Status);
        Assert.Equal(SettingsStatus.Forbidden, (await service.RemoveBackgroundAsync(Editor, CancellationToken.None)).Status);
        Assert.Equal(SettingsStatus.Forbidden, (await service.AddSiteAsync(Editor, "Autre", CancellationToken.None)).Status);
        Assert.Equal(SettingsStatus.Forbidden, (await service.SetSiteActiveAsync(Editor, _store.Sites[0].Id, active: false, CancellationToken.None)).Status);

        Assert.Empty(_store.Settings);
        Assert.Single(_store.Sites);
        Assert.True(_store.Sites[0].IsActive);
        Assert.Equal(0, _store.Saves);
        Assert.All(_audit.Entries, e => Assert.Equal(("access.denied", "settings"), (e.Action, e.Target)));
        Assert.Equal(8, _audit.Entries.Count);
    }

    private sealed class FakeStore : ISettingsAdminStore, ISettingReader
    {
        public Dictionary<string, Setting> Settings { get; } = [];

        public List<Site> Sites { get; } = [];

        public int Saves { get; private set; }

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(Settings.ToDictionary(s => s.Key, s => s.Value.Value));

        public Task<Setting?> FindAsync(string key, CancellationToken cancellationToken) => Task.FromResult(Settings.GetValueOrDefault(key));

        public void Add(Setting setting) => Settings.Add(setting.Key, setting);

        public void Remove(Setting setting) => Settings.Remove(setting.Key);

        public Task<IReadOnlyList<SiteView>> ListSitesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SiteView>>([.. Sites.OrderBy(s => s.Label).Select(s => new SiteView(s.Id, s.Label, s.IsActive))]);

        public Task<Site?> FindSiteAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Sites.SingleOrDefault(s => s.Id == id));

        public Task<bool> SiteLabelExistsAsync(string label, CancellationToken cancellationToken) =>
            Task.FromResult(Sites.Any(s => string.Equals(s.Label, label, StringComparison.OrdinalIgnoreCase)));

        public void AddSite(Site site) => Sites.Add(site);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAudit : IAuditLog
    {
        public List<(string Action, string? Target, string? Detail)> Entries { get; } = [];

        public Task RecordAsync(string action, Guid? userId, string? target, string? detail, CancellationToken cancellationToken)
        {
            Entries.Add((action, target, detail));
            return Task.CompletedTask;
        }
    }
}
