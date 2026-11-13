using System.Text;
using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.Distribution;
using CseForms.Application.Notifications;
using CseForms.Application.Pdf;
using CseForms.Application.Settings;

namespace CseForms.Application.Tests;

public class FormLinkTests
{
    [Fact]
    public void Link_is_built_from_the_public_address_with_an_optional_site()
    {
        Assert.Equal("https://formulaires.example.test/f/inscription", FormLink.Build("https://formulaires.example.test/", "inscription"));
        Assert.Equal(
            "https://formulaires.example.test/f/inscription?site=Saint-%C3%89tienne%20%26%20Co",
            FormLink.Build("https://formulaires.example.test", "inscription", "site", "Saint-Étienne & Co"));
    }

    [Fact]
    public void Site_parameter_is_the_site_field_prefilled_by_url()
    {
        Assert.Equal("site", FormLink.SiteParameter(FictiveInscription.SchemaJson));
        Assert.Null(FormLink.SiteParameter("""{"sections":[{"id":"s","fields":[{"id":"lieu","type":"site","label":"Site"}]}]}"""));
        Assert.Null(FormLink.SiteParameter(null));
    }

    [Theory]
    [InlineData("Riom", "riom")]
    [InlineData("Saint-Étienne", "saint-etienne")]
    [InlineData("  Site n°2 / Nord ", "site-n-2-nord")]
    [InlineData("***", "site")]
    public void File_name_part_is_plain_ascii(string text, string expected) =>
        Assert.Equal(expected, FormLink.FileNamePart(text));
}

public class DistributionServiceTests
{
    private static readonly CurrentUser Editor = new(Guid.NewGuid(), IsAdmin: false);
    private static readonly CurrentUser Reader = new(Guid.NewGuid(), IsAdmin: false, IsReadOnly: true);

    private readonly FakeBackOffice _backOffice = new();
    private readonly FakeStore _store = new();
    private readonly FakeAudit _audit = new();
    private readonly FakeQr _qr = new();
    private readonly FakeConverter _converter = new();
    private readonly Dictionary<string, string> _settings = new()
    {
        [SettingKeys.PublicBaseUrl] = "https://formulaires.example.test",
        [SettingKeys.CseName] = "CSE Démo",
        [SettingKeys.CseEmail] = "cse@example.test",
    };

    private DistributionService Service() =>
        new(_backOffice, _store, _audit, _qr, _converter, new FakeHasher(), new FakeSettings(_settings));

    [Fact]
    public async Task Page_gives_link_sites_and_access_mode()
    {
        var page = (await Service().GetPageAsync(Editor, _store.Form.Id, default)).Value!;

        Assert.Equal("https://formulaires.example.test/f/inscription", page.PublicUrl);
        Assert.Equal("site", page.SiteParameter);
        Assert.Equal(["Riom", "Saint-Étienne"], page.Sites);
        Assert.True(page.RequiresAccessCode);
    }

    [Fact]
    public async Task Qr_code_encodes_the_link_with_the_chosen_site()
    {
        var global = await Service().QrCodeAsync(Editor, _store.Form.Id, null, QrFormat.Png, default);
        var bySite = await Service().QrCodeAsync(Editor, _store.Form.Id, " Saint-Étienne ", QrFormat.Svg, default);

        Assert.Equal(["https://formulaires.example.test/f/inscription", "https://formulaires.example.test/f/inscription?site=Saint-%C3%89tienne"], _qr.Texts);
        Assert.Equal(("qr-inscription.png", "image/png"), (global.Value!.FileName, global.Value.ContentType));
        Assert.Equal(("qr-inscription-saint-etienne.svg", "image/svg+xml"), (bySite.Value!.FileName, bySite.Value.ContentType));
        Assert.Equal(2, _audit.Entries.Count(e => e == (AuditActions.QrCodeDownloaded, $"form:{_store.Form.Id}")));
    }

    [Theory]
    [InlineData("Site inconnu")]
    [InlineData("Aulnat")]
    [InlineData("riom")]
    public async Task Only_active_sites_are_accepted(string site)
    {
        var result = await Service().QrCodeAsync(Editor, _store.Form.Id, site, QrFormat.Png, default);

        Assert.Equal(DistributionStatus.Invalid, result.Status);
        Assert.Empty(_qr.Texts);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Form_without_site_prefill_has_no_qr_code_per_site()
    {
        _store.Form = _store.Form with { CurrentSchemaJson = """{"sections":[{"id":"s","fields":[{"id":"nom","type":"text","label":"Nom"}]}]}""" };

        var page = (await Service().GetPageAsync(Editor, _store.Form.Id, default)).Value!;
        var result = await Service().QrCodeAsync(Editor, _store.Form.Id, "Riom", QrFormat.Png, default);

        Assert.Empty(page.Sites);
        Assert.Equal(DistributionStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Missing_public_address_is_reported()
    {
        _settings.Remove(SettingKeys.PublicBaseUrl);

        var page = (await Service().GetPageAsync(Editor, _store.Form.Id, default)).Value!;
        var result = await Service().QrCodeAsync(Editor, _store.Form.Id, null, QrFormat.Png, default);

        Assert.Null(page.PublicUrl);
        Assert.Equal(DistributionService.MissingBaseUrl, result.Error);
    }

    [Fact]
    public async Task Readers_are_forbidden_and_other_forms_not_found()
    {
        Assert.Equal(DistributionStatus.Forbidden, (await Service().QrCodeAsync(Reader, _store.Form.Id, null, QrFormat.Png, default)).Status);

        _backOffice.CanAccess = false;
        Assert.Equal(DistributionStatus.NotFound, (await Service().FlyerAsync(Editor, _store.Form.Id, null, "482193", default)).Status);
        Assert.Equal(DistributionStatus.NotFound, (await Service().GetPageAsync(Editor, Guid.NewGuid(), default)).Status);

        Assert.Equal(2, _audit.Entries.Count(e => e.Action == AuditActions.AccessDenied));
        Assert.Empty(_qr.Texts);
        Assert.Empty(_converter.Documents);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("000000")]
    public async Task Flyer_of_a_protected_form_needs_its_access_code(string? code)
    {
        var result = await Service().FlyerAsync(Editor, _store.Form.Id, null, code, default);

        Assert.Equal(DistributionStatus.Invalid, result.Status);
        Assert.Empty(_converter.Documents);
        Assert.DoesNotContain(_audit.Entries, e => e.Action == AuditActions.FlyerGenerated);
    }

    [Fact]
    public async Task Wrong_code_is_audited_without_the_code()
    {
        await Service().FlyerAsync(Editor, _store.Form.Id, null, "000000", default);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal((AuditActions.FlyerCodeRejected, $"form:{_store.Form.Id}"), entry);
    }

    [Fact]
    public async Task Flyer_prints_title_site_readable_link_code_and_contact()
    {
        var result = await Service().FlyerAsync(Editor, _store.Form.Id, "Saint-Étienne", " 482193 ", default);

        Assert.Equal(("flyer-inscription-saint-etienne.pdf", "application/pdf"), (result.Value!.FileName, result.Value.ContentType));
        var html = Assert.Single(_converter.Documents).Body;
        Assert.Contains("<h1>Fiche d&#x27;inscription &lt;CSE&gt;</h1>", html);
        Assert.Contains("Site : Saint-Étienne", html);
        Assert.Contains("<strong>482193</strong>", html);
        Assert.Contains("formulaires.example.test/f/inscription?site=Saint-Étienne", html);
        Assert.Contains("<svg data-qr=\"https://formulaires.example.test/f/inscription?site=Saint-%C3%89tienne\"></svg>", html);
        Assert.Contains("Contact : cse@example.test", html);
        Assert.Contains("default-src 'none'", html);
        Assert.Contains(_audit.Entries, e => e.Action == AuditActions.FlyerGenerated);
    }

    [Fact]
    public async Task Public_form_flyer_prints_no_code()
    {
        _store.Form = _store.Form with { AccessCodeHash = null };

        await Service().FlyerAsync(Editor, _store.Form.Id, null, "482193", default);

        var html = Assert.Single(_converter.Documents).Body;
        Assert.DoesNotContain("482193", html);
        Assert.DoesNotContain("Code d'accès", html);
    }

    private sealed class FakeBackOffice : IBackOfficeStore
    {
        public bool CanAccess { get; set; } = true;

        public Task<bool> CanAccessFormAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken) => Task.FromResult(CanAccess);

        public Task<IReadOnlyList<FormSummary>> ListFormsAsync(CurrentUser user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FormHeader?> GetFormAsync(Guid formId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SubmissionPage> ListSubmissionsAsync(SubmissionQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SubmissionRecord?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PdfState> GetPdfStateAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StoredPdf?> GetPdfFileAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> GetUserDisplayNameAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RequestPdfAsync(Guid submissionId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> ChangeStatusAsync(Guid submissionId, string status, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(Guid submissionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ExportData> GetExportDataAsync(SubmissionQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeStore : IDistributionStore
    {
        public DistributionForm Form { get; set; } =
            new(Guid.NewGuid(), "inscription", "Fiche d'inscription <CSE>", "Published", FictiveInscription.SchemaJson, "hash:482193");

        public Task<DistributionForm?> GetFormAsync(Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult(formId == Form.Id ? Form : null);

        public Task<IReadOnlyList<string>> GetActiveSiteLabelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["Riom", "Saint-Étienne"]);
    }

    private sealed class FakeAudit : IAuditLog
    {
        public List<(string Action, string? Target)> Entries { get; } = [];

        public Task RecordAsync(string action, Guid? userId, string? target, string? detail, CancellationToken cancellationToken)
        {
            Entries.Add((action, target));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeQr : IQrCodeRenderer
    {
        public List<string> Texts { get; } = [];

        public byte[] Png(string text)
        {
            Texts.Add(text);
            return [0x89, 0x50, 0x4E, 0x47];
        }

        public string Svg(string text)
        {
            Texts.Add(text);
            return $"<svg data-qr=\"{text}\"></svg>";
        }
    }

    private sealed class FakeConverter : IPdfConverter
    {
        public List<PdfHtml> Documents { get; } = [];

        public Task<byte[]> ConvertAsync(PdfHtml html, CancellationToken cancellationToken)
        {
            Documents.Add(html);
            return Task.FromResult(Encoding.ASCII.GetBytes("%PDF-fictif"));
        }
    }

    private sealed class FakeHasher : IAccessCodeHasher
    {
        public string Hash(string accessCode) => $"hash:{accessCode}";

        public bool Verify(string hash, string accessCode) => hash == $"hash:{accessCode}";
    }

    private sealed class FakeSettings(IReadOnlyDictionary<string, string> values) : ISettingReader
    {
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(values);
    }
}
