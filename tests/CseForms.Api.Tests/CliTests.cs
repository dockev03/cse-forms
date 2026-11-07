using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CseForms.Api.Cli;
using CseForms.Domain.Forms;
using CseForms.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CseForms.Api.Tests;

public sealed class CliTests(ApiFactory factory) : IClassFixture<ApiFactory>, IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("cse-forms-cli-").FullName;

    private string LastOutput { get; set; } = string.Empty;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private async Task<int> RunAsync(string stdin, params string[] args)
    {
        var output = new StringWriter();
        var code = await CliSupport.RunAsync(factory.Services, args, new StringReader(stdin), output, interactive: false);
        LastOutput = output.ToString();
        return code;
    }

    private string WriteForm(string slug, string label = "Nom", string access = "public")
    {
        var path = Path.Combine(_directory, $"{slug}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $$"""
            {"slug":"{{slug}}","title":"Formulaire fictif","caseNumberPrefix":"TST","access":{"mode":"{{access}}"},
             "sections":[{"id":"s","fields":[{"id":"nom","type":"text","label":"{{label}}","required":true}]}]}
            """);
        return path;
    }

    private static string InscriptionPath => Path.Combine(AppContext.BaseDirectory, "forms", "inscription.json");

    [Fact]
    public void Commands_are_recognised()
    {
        Assert.True(CliSupport.IsCommand(["forms", "list"]));
        Assert.True(CliSupport.IsCommand(["sites", "list"]));
        Assert.True(CliSupport.IsCommand(["settings", "list"]));
        Assert.True(CliSupport.IsCommand(["users", "list"]));
        Assert.True(CliSupport.IsCommand(["notifications", "show"]));
        Assert.False(CliSupport.IsCommand([]));
        Assert.False(CliSupport.IsCommand(["--urls", "https://127.0.0.1"]));
    }

    [Fact]
    public async Task Inscription_is_imported_with_its_access_code_and_accepts_a_fictive_submission()
    {
        foreach (var site in new[] { "Abrest", "Riom" })
        {
            Assert.Equal(0, await RunAsync(string.Empty, "sites", "add", "--label", site));
        }

        Assert.Equal(0, await RunAsync("4821", "forms", "import", "--file", InscriptionPath));
        Assert.Contains("créé et publié en version 1", LastOutput);
        Assert.DoesNotContain("4821", LastOutput);

        await using (var db = factory.Database.CreateContext())
        {
            var form = await db.Forms.Include(f => f.Versions).SingleAsync(f => f.Slug == "inscription");
            Assert.Equal("INS", form.CaseNumberPrefix);
            Assert.Equal(
                await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "forms", "inscription.pdf.html")),
                form.CurrentVersion!.PdfTemplateHtml);
            Assert.NotNull(form.AccessCodeHash);
            Assert.DoesNotContain("4821", form.AccessCodeHash);
        }

        var client = factory.CreateClient();
        var view = JsonNode.Parse(await client.GetStringAsync("/api/public/forms/inscription"))!;
        Assert.True(view["requiresAccessCode"]!.GetValue<bool>());
        Assert.True(view["ui"]!["multiStep"]!.GetValue<bool>());
        Assert.Null(view["definition"]!["slug"]);
        Assert.Null(view["definition"]!["access"]);

        var data = new JsonObject
        {
            ["civilite"] = "Mme",
            ["nom"] = "fictif",
            ["prenom"] = "camille",
            ["contrat"] = "CDD",
            ["dateEmbauche"] = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd"),
            ["dateFin"] = DateTime.UtcNow.AddMonths(6).ToString("yyyy-MM-dd"),
            ["site"] = "Riom",
            ["adresse"] = new JsonObject { ["street"] = "1 rue Fictive", ["postalCode"] = "63200", ["city"] = "Riom" },
            ["emailPerso"] = "Camille.Fictif@Example.test",
            ["emailConfirmation"] = "camille.fictif@example.test",
            ["enfants"] = new JsonArray(new JsonObject
            {
                ["nom"] = "fictif", ["prenom"] = "léa", ["naissance"] = "2020-05-01", ["sexe"] = "F",
            }),
            ["rgpdLu"] = true,
            ["attestation"] = true,
        };

        var denied = await client.PostAsJsonAsync("/api/public/forms/inscription/submissions", new { version = 1, accessCode = "0000", data });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var created = await client.PostAsJsonAsync("/api/public/forms/inscription/submissions", new { version = 1, accessCode = "4821", data });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var caseNumber = JsonNode.Parse(await created.Content.ReadAsStringAsync())!["caseNumber"]!.GetValue<string>();
        Assert.Matches(@"^INS-\d{4}-0001$", caseNumber);

        await using (var db = factory.Database.CreateContext())
        {
            var saved = JsonNode.Parse((await db.Submissions.SingleAsync(s => s.CaseNumber == caseNumber)).DataJson)!;
            Assert.Equal("FICTIF", saved["nom"]!.GetValue<string>());
            Assert.Equal("camille.fictif@example.test", saved["emailPerso"]!.GetValue<string>());
            Assert.Null(saved["emailConfirmation"]);
        }

        // Réimport du même fichier : rien ne change, le code n'est pas redemandé.
        Assert.Equal(0, await RunAsync(string.Empty, "forms", "import", "--file", InscriptionPath));
        Assert.Contains("reste en version 1", LastOutput);
    }

    [Fact]
    public async Task Modified_file_publishes_a_new_version_and_keeps_existing_submissions_on_theirs()
    {
        Assert.Equal(0, await RunAsync(string.Empty, "forms", "import", "--file", WriteForm("cli-version")));

        Guid firstVersionId;
        await using (var db = factory.Database.CreateContext())
        {
            var form = await db.Forms.Include(f => f.Versions).SingleAsync(f => f.Slug == "cli-version");
            firstVersionId = form.CurrentVersionId!.Value;
            db.Submissions.Add(form.CreateSubmission("TST-2026-0001", """{"nom":"FICTIF"}""", DateTimeOffset.UtcNow, 0));
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, await RunAsync(string.Empty, "forms", "import", "--file", WriteForm("cli-version", label: "Nom de famille")));
        Assert.Contains("publié en version 2", LastOutput);

        await using (var db = factory.Database.CreateContext())
        {
            var form = await db.Forms.Include(f => f.Versions).SingleAsync(f => f.Slug == "cli-version");
            Assert.Equal(2, form.Versions.Count);
            Assert.NotEqual(firstVersionId, form.CurrentVersionId);
            Assert.Equal(firstVersionId, (await db.Submissions.SingleAsync(s => s.CaseNumber == "TST-2026-0001")).FormVersionId);
        }
    }

    [Fact]
    public async Task Invalid_file_is_refused_with_its_errors()
    {
        var path = Path.Combine(_directory, "invalide.json");
        await File.WriteAllTextAsync(path, """{"slug":"cli-invalide","title":"T","access":{"mode":"public"},"sections":[],"inconnu":1}""");

        Assert.Equal(1, await RunAsync(string.Empty, "forms", "import", "--file", path));
        Assert.Contains("$.inconnu", LastOutput);

        await using var db = factory.Database.CreateContext();
        Assert.False(await db.Forms.AnyAsync(f => f.Slug == "cli-invalide"));
    }

    [Fact]
    public async Task Pin_form_without_a_valid_code_is_not_created()
    {
        Assert.Equal(1, await RunAsync("12", "forms", "import", "--file", WriteForm("cli-pin", access: "pin")));

        await using var db = factory.Database.CreateContext();
        Assert.False(await db.Forms.AnyAsync(f => f.Slug == "cli-pin"));
    }

    [Fact]
    public async Task Access_code_can_be_changed_and_is_only_stored_hashed()
    {
        Assert.Equal(0, await RunAsync("4821", "forms", "import", "--file", WriteForm("cli-code", access: "pin")));

        Assert.Equal(0, await RunAsync("975310", "forms", "access-code", "--form", "cli-code"));
        Assert.Equal(1, await RunAsync("abc", "forms", "access-code", "--form", "cli-code"));
        Assert.Equal(1, await RunAsync("4821", "forms", "access-code", "--form", "cli-inconnu"));

        await using var db = factory.Database.CreateContext();
        var hash = (await db.Forms.SingleAsync(f => f.Slug == "cli-code")).AccessCodeHash!;
        var hasher = factory.Services.GetRequiredService<CseForms.Application.Common.IAccessCodeHasher>();
        Assert.True(hasher.Verify(hash, "975310"));
        Assert.DoesNotContain("975310", hash);
    }

    [Fact]
    public async Task Forms_are_listed_without_access_code()
    {
        Assert.Equal(0, await RunAsync("4821", "forms", "import", "--file", WriteForm("cli-liste", access: "pin")));

        Assert.Equal(0, await RunAsync(string.Empty, "forms", "list"));

        Assert.Contains("cli-liste | Formulaire fictif | Published | v1 | TST | code d'accès", LastOutput);
        Assert.DoesNotContain("4821", LastOutput);
    }

    [Fact]
    public async Task Sites_are_added_once_and_disabled_rather_than_deleted()
    {
        Assert.Equal(0, await RunAsync(string.Empty, "sites", "add", "--label", "  Site CLI  "));
        Assert.Equal(1, await RunAsync(string.Empty, "sites", "add", "--label", "Site CLI"));
        Assert.Equal(0, await RunAsync(string.Empty, "sites", "disable", "--label", "Site CLI"));
        Assert.Equal(1, await RunAsync(string.Empty, "sites", "disable", "--label", "Site inconnu"));

        Assert.Equal(0, await RunAsync(string.Empty, "sites", "list"));
        Assert.Contains("Site CLI | inactif", LastOutput);

        Assert.Equal(0, await RunAsync(string.Empty, "sites", "enable", "--label", "Site CLI"));
        await using var db = factory.Database.CreateContext();
        Assert.True((await db.Sites.SingleAsync(s => s.Label == "Site CLI")).IsActive);
    }

    [Fact]
    public async Task Settings_accept_only_known_keys_and_valid_values()
    {
        Assert.Equal(0, await RunAsync(string.Empty, "settings", "set", "--key", "cse.name", "--value", "CSE fictif"));
        Assert.Equal(0, await RunAsync("Secrétaire : Prénom Fictif  \r\nTrésorier : Autre Fictif\r\n\r\n", "settings", "set", "--key", "cse.office"));
        Assert.Equal(0, await RunAsync(string.Empty, "settings", "set", "--key", "app.publicBaseUrl", "--value", "https://formulaires.example.test/"));

        Assert.Equal(1, await RunAsync(string.Empty, "settings", "set", "--key", "smtp.password", "--value", "x"));
        Assert.Equal(1, await RunAsync(string.Empty, "settings", "set", "--key", "app.publicBaseUrl", "--value", "http://formulaires.example.test"));
        Assert.Equal(1, await RunAsync(string.Empty, "settings", "set", "--key", "cse.email", "--value", "pas-un-email"));

        await using (var db = factory.Database.CreateContext())
        {
            var values = await db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);
            Assert.Equal("Secrétaire : Prénom Fictif\nTrésorier : Autre Fictif", values["cse.office"]);
            Assert.Equal("https://formulaires.example.test", values["app.publicBaseUrl"]);
            Assert.False(values.ContainsKey("smtp.password"));
        }

        Assert.Equal(0, await RunAsync(string.Empty, "settings", "unset", "--key", "cse.name"));
        Assert.Equal(0, await RunAsync(string.Empty, "settings", "list"));
        Assert.Contains("cse.name | (non défini)", LastOutput);
        Assert.Contains("cse.office | Secrétaire : Prénom Fictif / Trésorier : Autre Fictif", LastOutput);
    }

    [Fact]
    public async Task Pdf_template_is_read_next_to_the_form_file_and_checked()
    {
        var path = Path.Combine(_directory, "cli-modele.json");
        await File.WriteAllTextAsync(path, """
            {"slug":"cli-modele","title":"T","access":{"mode":"public"},"pdfTemplate":"cli-modele.pdf.html",
             "sections":[{"id":"s","fields":[{"id":"nom","type":"text","label":"Nom"}]}]}
            """);

        Assert.Equal(1, await RunAsync(string.Empty, "forms", "import", "--file", path));
        Assert.Contains("$.pdfTemplate", LastOutput);

        await File.WriteAllTextAsync(Path.Combine(_directory, "cli-modele.pdf.html"), "<p>{{ field.nom }}</p><script></script>");
        Assert.Equal(1, await RunAsync(string.Empty, "forms", "import", "--file", path));
        Assert.Contains("Balise interdite", LastOutput);

        await File.WriteAllTextAsync(Path.Combine(_directory, "cli-modele.pdf.html"), "<p>{{ field.nom }}</p>");
        Assert.Equal(0, await RunAsync(string.Empty, "forms", "import", "--file", path));

        await using var db = factory.Database.CreateContext();
        var form = await db.Forms.Include(f => f.Versions).SingleAsync(f => f.Slug == "cli-modele");
        Assert.Equal("<p>{{ field.nom }}</p>", form.CurrentVersion!.PdfTemplateHtml);
    }

    [Fact]
    public async Task Logo_is_stored_as_a_data_uri_only_from_a_png_or_jpeg_file()
    {
        var png = Path.Combine(_directory, "logo.png");
        await File.WriteAllBytesAsync(png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);
        var svg = Path.Combine(_directory, "logo.svg");
        await File.WriteAllTextAsync(svg, "<svg onload=\"x()\"></svg>");

        Assert.Equal(1, await RunAsync(string.Empty, "settings", "logo", "--file", svg));
        Assert.Equal(1, await RunAsync(string.Empty, "settings", "set", "--key", "cse.logo", "--value", "data:image/png;base64,AAAA"));
        Assert.Contains("settings logo", LastOutput);

        Assert.Equal(0, await RunAsync(string.Empty, "settings", "logo", "--file", png));
        await using (var db = factory.Database.CreateContext())
        {
            Assert.StartsWith("data:image/png;base64,", (await db.Settings.SingleAsync(s => s.Key == "cse.logo")).Value);
        }

        Assert.Equal(0, await RunAsync(string.Empty, "settings", "list"));
        Assert.Contains("cse.logo | (image, 1 Ko)", LastOutput);
        Assert.DoesNotContain("base64", LastOutput);
    }

    [Fact]
    public async Task Notification_rule_is_set_shown_and_removed()
    {
        Assert.Equal(0, await RunAsync(string.Empty, "forms", "import", "--file", WriteForm("cli-notif")));

        Assert.Equal(1, await RunAsync(string.Empty, "notifications", "set", "--form", "cli-inconnu", "--to", "elu@example.test"));
        Assert.Equal(1, await RunAsync(string.Empty, "notifications", "set", "--form", "cli-notif", "--to", "pas-une-adresse"));
        Assert.Equal(1, await RunAsync(string.Empty, "notifications", "set", "--form", "cli-notif", "--to", "elu@example.test", "--subject", "Inscription de {nom}"));
        Assert.Contains("{nom}", LastOutput);

        var bodyFile = Path.Combine(_directory, "corps.txt");
        await File.WriteAllTextAsync(bodyFile, "Nouvelle réponse {numero} du {date}.\r\n");
        Assert.Equal(0, await RunAsync(string.Empty, "notifications", "set", "--form", "cli-notif",
            "--to", "Elu@Example.test; autre@example.test", "--subject", "Dossier {numero}, site {site}", "--body-file", bodyFile));

        Assert.Equal(0, await RunAsync(string.Empty, "notifications", "show", "--form", "cli-notif"));
        Assert.Contains("Destinataires : elu@example.test, autre@example.test", LastOutput);
        Assert.Contains("Objet : Dossier {numero}, site {site}", LastOutput);
        Assert.Contains("Nouvelle réponse {numero} du {date}.", LastOutput);

        // Une nouvelle règle remplace la précédente.
        Assert.Equal(0, await RunAsync(string.Empty, "notifications", "set", "--form", "cli-notif", "--to", "elu@example.test"));
        await using (var db = factory.Database.CreateContext())
        {
            var formId = (await db.Forms.SingleAsync(f => f.Slug == "cli-notif")).Id;
            var rule = await db.NotificationRules.SingleAsync(r => r.FormId == formId);
            Assert.Equal(["elu@example.test"], rule.Recipients);
            Assert.Null(rule.Subject);
        }

        Assert.Equal(0, await RunAsync(string.Empty, "notifications", "remove", "--form", "cli-notif"));
        Assert.Equal(0, await RunAsync(string.Empty, "notifications", "show", "--form", "cli-notif"));
        Assert.Contains("Aucune notification", LastOutput);
    }

    [Fact]
    public async Task Smtp_and_alert_settings_are_checked()
    {
        Assert.Equal(0, await RunAsync(string.Empty, "settings", "set", "--key", "smtp.host", "--value", "smtp.example.test"));
        Assert.Equal(0, await RunAsync(string.Empty, "settings", "set", "--key", "smtp.port", "--value", "587"));
        Assert.Equal(0, await RunAsync(string.Empty, "settings", "set", "--key", "smtp.security", "--value", "starttls"));
        Assert.Equal(0, await RunAsync(string.Empty, "settings", "set", "--key", "smtp.from", "--value", "formulaires@example.test"));
        Assert.Equal(0, await RunAsync(string.Empty, "settings", "set", "--key", "alerts.email", "--value", "admin@example.test"));

        Assert.Equal(1, await RunAsync(string.Empty, "settings", "set", "--key", "smtp.host", "--value", "smtp://smtp.example.test:587"));
        Assert.Equal(1, await RunAsync(string.Empty, "settings", "set", "--key", "smtp.port", "--value", "70000"));
        Assert.Equal(1, await RunAsync(string.Empty, "settings", "set", "--key", "smtp.security", "--value", "tls"));
        Assert.Equal(1, await RunAsync(string.Empty, "settings", "set", "--key", "smtp.from", "--value", "CSE <formulaires@example.test>"));
        Assert.Equal(1, await RunAsync(string.Empty, "settings", "set", "--key", "smtp.password", "--value", "secret"));
    }
}
