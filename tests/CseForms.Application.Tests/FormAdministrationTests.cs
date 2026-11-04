using System.Text.Json.Nodes;
using CseForms.Application.Common;
using CseForms.Application.FormAdministration;
using CseForms.Application.FormDefinitions;
using CseForms.Domain.Forms;
using Microsoft.Extensions.Logging.Abstractions;

namespace CseForms.Application.Tests;

public class FormPackageReaderTests
{
    private const string Sections = """[{"id":"s","fields":[{"id":"nom","type":"text","label":"Nom"}]}]""";

    private static string Package(string extra = "") =>
        $$"""{"slug":"essai","title":"Formulaire fictif","access":{"mode":"public"}{{extra}},"sections":{{Sections}}}""";

    public static string InscriptionJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "forms", "inscription.json"));

    [Fact]
    public void Inscription_file_is_a_valid_package()
    {
        var result = FormPackageReader.Read(InscriptionJson());

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors.Select(e => $"{e.Path} {e.Message}")));
        var package = result.Package!;
        Assert.Equal("inscription", package.Slug);
        Assert.Equal("INS", package.CaseNumberPrefix);
        Assert.Equal(AccessMode.Pin, package.AccessMode);

        var definition = FormDefinitionParser.Parse(package.SchemaJson).Definition!;
        var fields = definition.AllTopLevelFields.Select(f => f.Id).ToList();
        Assert.Equal(
            ["civilite", "nom", "prenom", "contrat", "dateEmbauche", "dateFin", "site", "adresse", "emailPerso", "emailConfirmation",
             "conjointNom", "conjointPrenom", "enfants", "rgpdNotice", "rgpdLu", "attestation"],
            fields);
        Assert.Equal(10, definition.AllTopLevelFields.Single(f => f.Id == "enfants").Max);
        Assert.Contains("Toute modification d'état civil doit être communiquée au CSE", JsonNode.Parse(package.UiJson!)!["confirmationText"]!.GetValue<string>());
    }

    [Fact]
    public void Form_settings_are_kept_out_of_the_versioned_definition()
    {
        var package = FormPackageReader.Read(Package(""","caseNumberPrefix":"ESS","retentionDays":30,"ui":{"multiStep":true}""")).Package!;

        var schema = JsonNode.Parse(package.SchemaJson)!.AsObject();
        Assert.Equal(["title", "sections"], schema.Select(p => p.Key));
        Assert.Equal("""{"multiStep":true}""", package.UiJson);
        Assert.Equal(30, package.RetentionDays);
        Assert.Equal("ESS", package.CaseNumberPrefix);
    }

    [Theory]
    [InlineData(""","slugg":"x" """, "$.slugg")]
    [InlineData(""","notifications":[{"to":["cse@example.test"]}]""", "$.notifications")]
    [InlineData(""","retentionDays":0""", "$.retentionDays")]
    [InlineData(""","ui":{"theme":"dark"}""", "$.ui.theme")]
    [InlineData(""","ui":{"multiStep":"oui"}""", "$.ui.multiStep")]
    public void Invalid_packages_are_refused_with_a_path(string extra, string path)
    {
        var result = FormPackageReader.Read(Package(extra));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path == path);
    }

    [Theory]
    [InlineData("""{"slug":"essai","title":"T","sections":[]}""")]
    [InlineData("""{"slug":"essai","title":"T","access":{"mode":"ouvert"},"sections":[]}""")]
    public void Access_mode_must_be_explicit(string json)
    {
        var result = FormPackageReader.Read(json);

        Assert.Contains(result.Errors, e => e.Path.StartsWith("$.access", StringComparison.Ordinal));
    }

    [Fact]
    public void Definition_errors_are_reported()
    {
        var result = FormPackageReader.Read("""{"slug":"essai","title":"T","access":{"mode":"public"},"sections":[{"id":"s","fields":[{"id":"x","type":"inconnu"}]}]}""");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("[]")]
    public void Unreadable_files_are_refused(string json) => Assert.False(FormPackageReader.Read(json).IsValid);
}

public class FormAdminServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();

    private FormAdminService Service() =>
        new(_store, new FakeHasher(), new FixedTimeProvider(Now), NullLogger<FormAdminService>.Instance);

    private static FormPackage Package(AccessMode mode = AccessMode.Public, string title = "Formulaire fictif", string label = "Nom") =>
        FormPackageReader.Read($$"""
            {"slug":"essai","title":"{{title}}","caseNumberPrefix":"ESS","retentionDays":30,
             "access":{"mode":"{{(mode == AccessMode.Pin ? "pin" : "public")}}"},
             "sections":[{"id":"s","fields":[{"id":"nom","type":"text","label":"{{label}}"}]}]}
            """).Package!;

    [Fact]
    public async Task First_import_creates_and_publishes_the_form()
    {
        var outcome = await Service().ImportAsync(Package(), null, "cli", default);

        Assert.Equal(new ImportOutcome.Published("essai", 1, Created: true), outcome);
        var form = Assert.Single(_store.Forms);
        Assert.Equal(FormStatus.Published, form.Status);
        Assert.Equal("ESS", form.CaseNumberPrefix);
        Assert.Equal(30, form.RetentionDays);
        Assert.Null(form.AccessCodeHash);
        Assert.Equal("cli", form.CurrentVersion!.CreatedBy);
    }

    [Fact]
    public async Task Same_definition_does_not_create_a_version()
    {
        await Service().ImportAsync(Package(), null, "cli", default);

        var outcome = await Service().ImportAsync(Package(), null, "cli", default);

        Assert.Equal(new ImportOutcome.Unchanged("essai", 1), outcome);
        Assert.Single(_store.Forms[0].Versions);
    }

    [Fact]
    public async Task Changed_definition_creates_a_new_version_and_keeps_the_old_one()
    {
        await Service().ImportAsync(Package(), null, "cli", default);
        var first = _store.Forms[0].CurrentVersion!;

        var outcome = await Service().ImportAsync(Package(label: "Nom de famille"), null, "cli", default);

        Assert.Equal(new ImportOutcome.Published("essai", 2, Created: false), outcome);
        var form = _store.Forms[0];
        Assert.Equal(2, form.Versions.Count);
        Assert.Same(first, form.Versions.Single(v => v.Number == 1));
        Assert.Contains("Nom de famille", form.CurrentVersion!.SchemaJson);
    }

    [Fact]
    public async Task New_version_does_not_reopen_a_closed_form()
    {
        await Service().ImportAsync(Package(), null, "cli", default);
        _store.Forms[0].Close();

        await Service().ImportAsync(Package(label: "Nom de famille"), null, "cli", default);

        Assert.Equal(FormStatus.Closed, _store.Forms[0].Status);
    }

    [Fact]
    public async Task Pin_form_is_never_published_without_an_access_code()
    {
        var outcome = await Service().ImportAsync(Package(AccessMode.Pin), null, "cli", default);

        Assert.IsType<ImportOutcome.AccessCodeRequired>(outcome);
        Assert.Empty(_store.Forms);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Pin_form_stores_only_the_hash_and_keeps_it_on_reimport()
    {
        await Service().ImportAsync(Package(AccessMode.Pin), "4821", "cli", default);
        Assert.Equal("hash:4821", _store.Forms[0].AccessCodeHash);

        var outcome = await Service().ImportAsync(Package(AccessMode.Pin, title: "Nouveau titre"), null, "cli", default);

        Assert.IsType<ImportOutcome.Published>(outcome);
        Assert.Equal("hash:4821", _store.Forms[0].AccessCodeHash);
        Assert.Equal("Nouveau titre", _store.Forms[0].Title);
    }

    [Fact]
    public async Task Public_mode_removes_the_access_code()
    {
        await Service().ImportAsync(Package(AccessMode.Pin), "4821", "cli", default);

        await Service().ImportAsync(Package(AccessMode.Public), null, "cli", default);

        Assert.Null(_store.Forms[0].AccessCodeHash);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234567890123")]
    [InlineData("12a4")]
    public async Task Access_code_must_be_4_to_12_digits(string code)
    {
        var outcome = await Service().ImportAsync(Package(AccessMode.Pin), code, "cli", default);

        Assert.IsType<ImportOutcome.Invalid>(outcome);
        Assert.Empty(_store.Forms);
    }

    [Fact]
    public async Task Access_code_can_be_changed()
    {
        await Service().ImportAsync(Package(AccessMode.Pin), "4821", "cli", default);

        Assert.Equal(AccessCodeOutcome.Updated, await Service().SetAccessCodeAsync(" ESSAI ", "975310", default));
        Assert.Equal("hash:975310", _store.Forms[0].AccessCodeHash);
        Assert.Equal(AccessCodeOutcome.Invalid, await Service().SetAccessCodeAsync("essai", "12", default));
        Assert.Equal(AccessCodeOutcome.NotFound, await Service().SetAccessCodeAsync("inconnu", "4821", default));
    }

    [Fact]
    public async Task Invalid_prefix_is_reported_without_saving()
    {
        var package = Package() with { CaseNumberPrefix = "1" };

        var outcome = await Service().ImportAsync(package, null, "cli", default);

        Assert.IsType<ImportOutcome.Invalid>(outcome);
        Assert.Equal(0, _store.SaveCount);
    }

    private sealed class FakeStore : IFormAdminStore
    {
        public List<Form> Forms { get; } = [];

        public int SaveCount { get; private set; }

        public Task<Form?> FindBySlugAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(Forms.SingleOrDefault(f => f.Slug == slug));

        public void Add(Form form) => Forms.Add(form);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FormListItem>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeHasher : IAccessCodeHasher
    {
        public string Hash(string accessCode) => $"hash:{accessCode}";

        public bool Verify(string hash, string accessCode) => hash == Hash(accessCode);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
