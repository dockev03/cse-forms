using System.Text.Json.Nodes;
using CseForms.Application.FormAdministration;
using CseForms.Application.FormDefinitions;
using CseForms.Application.Pdf;
using CseForms.Application.Settings;

namespace CseForms.Application.Tests;

public class PdfTemplateTests
{
    // 14:05 UTC le 4 novembre 2026, soit 15:05 à Paris.
    private static readonly DateTimeOffset Submitted = new(2026, 11, 4, 14, 5, 0, TimeSpan.Zero);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    private const string SimpleSchema = """
        {"title":"Fictif","sections":[{"id":"s","fields":[
          {"id":"nom","type":"text","label":"Nom"},
          {"id":"accord","type":"consent","label":"Accord"},
          {"id":"enfants","type":"repeat","label":"Enfant","fields":[{"id":"prenom","type":"text","label":"Prénom"}]}
        ]}]}
        """;

    private static FormDefinition Simple => FormDefinitionParser.Parse(SimpleSchema).Definition!;

    private static FormPackage Inscription =>
        FormPackageReader.Read(FormPackageReaderTests.InscriptionJson(), FormPackageReaderTests.ReadFormsFile).Package!;

    private static PdfSource Source(string schemaJson, string? template, string dataJson, IReadOnlyDictionary<string, string>? settings = null) =>
        new(Guid.NewGuid(), "Fiche d'inscription au CSE", "INS-2026-0042", 2, Submitted, schemaJson, dataJson,
            settings ?? new Dictionary<string, string>(), template);

    private static string RenderInscription(string dataJson, IReadOnlyDictionary<string, string>? settings = null)
    {
        var html = SubmissionPdfRenderer.Render(Source(Inscription.SchemaJson, Inscription.PdfTemplateHtml, dataJson, settings), out var rejected).Body;
        Assert.False(rejected);
        return html;
    }

    private const string FictiveCdd = """
        {"civilite":"Mme","nom":"FICTIF","prenom":"Camille","contrat":"CDD","dateEmbauche":"2026-09-01","dateFin":"2027-08-31",
         "site":"Riom","adresse":{"street":"1 rue de l'Exemple","postalCode":"63200","city":"RIOM"},"emailPerso":"camille@example.test",
         "enfants":[{"nom":"FICTIF","prenom":"Léa","naissance":"2020-05-01","sexe":"F"},{"nom":"FICTIF","prenom":"Tom","naissance":"2022-01-15"}],
         "rgpdLu":true,"attestation":true}
        """;

    [Fact]
    public void Inscription_template_lays_out_the_form()
    {
        var html = RenderInscription(FictiveCdd);

        Assert.Contains("Dossier n° <strong>INS-2026-0042</strong>", html);
        Assert.Contains("Soumis le 04/11/2026 à 15:05", html);
        Assert.Contains("Formulaire soumis en ligne", html);
        Assert.Contains("<span class=\"valeur\">31/08/2027</span>", html);
        Assert.Contains("1 rue de l&#x27;Exemple, 63200 RIOM", html);
        Assert.Contains("<td class=\"num\">2</td><td>FICTIF</td><td>Tom</td><td>15/01/2022</td><td></td>", html);
        Assert.Contains("Non renseigné", html);
        Assert.Contains("☑</span> J&#x27;atteste l&#x27;exactitude des informations", html);
        Assert.Contains("Toute modification d'état civil doit être communiquée au CSE.", html);
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public void Inscription_template_handles_absent_answers()
    {
        var html = RenderInscription("""{"nom":"FICTIF","contrat":"CDI"}""");

        Assert.Contains("Sans objet", html);
        Assert.Contains("Aucun enfant déclaré", html);
        Assert.DoesNotContain("<table>", html);
        Assert.Contains("☐</span> J&#x27;atteste", html);
        Assert.DoesNotContain("Bureau du CSE", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void Inscription_template_shows_logo_and_office_from_settings()
    {
        var settings = new Dictionary<string, string>
        {
            [SettingKeys.CseName] = "CSE fictif",
            [SettingKeys.CseOffice] = "Secrétaire : Prénom Fictif",
            [SettingKeys.CseLogo] = CseLogo.ToDataUri(Png)!,
        };

        var html = RenderInscription("""{"nom":"FICTIF"}""", settings);

        Assert.Contains("<div class=\"cse-nom\">CSE fictif</div>", html);
        Assert.Contains("<img class=\"cse-logo\" src=\"data:image/png;base64,", html);
        Assert.Contains("Secrétaire : Prénom Fictif", html);
    }

    [Fact]
    public void Template_document_loads_nothing_from_outside()
    {
        var pdf = SubmissionPdfRenderer.Render(Source(Inscription.SchemaJson, Inscription.PdfTemplateHtml, FictiveCdd));

        Assert.StartsWith("<!doctype html>", pdf.Body);
        Assert.Contains("default-src 'none'", pdf.Body);
        Assert.DoesNotContain("<script", pdf.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", pdf.Body.Replace("http-equiv", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dossier INS-2026-0042", pdf.Footer);
    }

    [Fact]
    public void Entered_values_are_encoded_never_interpreted()
    {
        var html = RenderInscription("""{"nom":"<script>alert(1)</script>","prenom":"<img src=x onerror=y>","enfants":[{"prenom":"{{ cse.logo }}"}]}""");

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("<td>{{ cse.logo }}</td>", html);
    }

    [Fact]
    public void Blocks_follow_presence_of_values()
    {
        var template = PdfTemplate.Parse(
            "{{#if field.nom}}[{{ field.nom }}]{{else}}sans nom{{/if}}|{{#if field.accord}}accord{{else}}refus{{/if}}|"
            + "{{#each field.enfants}}{{ item.index }}={{#if item.prenom}}{{ item.prenom }}{{else}}?{{/if}};{{/each}}|{{ label.nom }}",
            Simple).Template!;

        string Render(string json) => template.Render(Source(SimpleSchema, null, json), Simple, (JsonObject)JsonNode.Parse(json)!);

        Assert.Equal("[A&amp;B]|accord|1=Léa;2=?;|Nom", Render("""{"nom":"A&B","accord":true,"enfants":[{"prenom":"Léa"},{}]}"""));
        Assert.Equal("sans nom|refus||Nom", Render("""{"accord":false}"""));
    }

    [Theory]
    [InlineData("<script>x</script>", "Balise interdite")]
    [InlineData("<SCRIPT src=x>", "Balise interdite")]
    [InlineData("<img src=\"https://example.test/x.png\">", "Balise interdite")]
    [InlineData("<link rel=stylesheet>", "Balise interdite")]
    [InlineData("<svg></svg>", "Balise interdite")]
    [InlineData("<iframe></iframe>", "Balise interdite")]
    [InlineData("<meta http-equiv=refresh>", "Balise interdite")]
    [InlineData("<div onload=\"x()\">", "Attribut d'événement")]
    [InlineData("<a href=\"https://example.test\">lien</a>", "Attribut de ressource")]
    [InlineData("<div style=\"background: url(https://example.test/x.png)\">", "Ressource CSS")]
    [InlineData("<style>@import 'x.css';</style>", "Ressource CSS")]
    [InlineData("<p>javascript:alert(1)</p>", "javascript:")]
    public void Markup_that_could_load_or_run_something_is_refused(string template, string expected)
    {
        var result = PdfTemplate.Parse(template, Simple);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{{ field.inconnu }}", "absent de la définition")]
    [InlineData("{{ field.enfants }}", "#each")]
    [InlineData("{{ item.prenom }}", "n'est utilisable que dans")]
    [InlineData("{{#each field.nom}}{{/each}}", "n'est pas un groupe répétable")]
    [InlineData("{{#each field.enfants}}{{ item.age }}{{/each}}", "absent du groupe")]
    [InlineData("{{#each field.enfants}}{{#each field.enfants}}{{/each}}{{/each}}", "imbriqués")]
    [InlineData("{{#if field.nom}}ouvert", "non fermé")]
    [InlineData("{{#if field.nom}}x{{/each}}", "inattendu")]
    [InlineData("texte{{/if}}", "sans bloc ouvert")]
    [InlineData("{{ field.nom", "non fermée")]
    [InlineData("{{ field.nom }} }}", "non fermée")]
    [InlineData("{{ un nom }}", "incorrecte")]
    [InlineData("{{ secret.valeur }}", "inconnu")]
    [InlineData("{{#if label.nom}}x{{/if}}", "inconnu")]
    [InlineData("", "vide")]
    public void Template_errors_are_reported(string template, string expected)
    {
        var result = PdfTemplate.Parse(template, Simple);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Errors_give_the_line()
    {
        var result = PdfTemplate.Parse("<p>{{ field.nom }}</p>\n<p>\n{{ field.inconnu }}</p>", Simple);

        Assert.StartsWith("Ligne 3 :", Assert.Single(result.Errors));
    }

    [Fact]
    public void Oversized_template_is_refused()
    {
        var result = PdfTemplate.Parse(new string('x', PdfTemplate.MaxLength + 1), Simple);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Invalid_stored_template_falls_back_to_the_generic_layout()
    {
        var pdf = SubmissionPdfRenderer.Render(Source(SimpleSchema, "<p>{{ field.supprime }}</p>", """{"nom":"FICTIF"}"""), out var rejected);

        Assert.True(rejected);
        Assert.Contains("<table class=\"values\">", pdf.Body);
        Assert.Contains("FICTIF", pdf.Body);
    }

    [Fact]
    public void Version_without_template_uses_the_generic_layout()
    {
        var pdf = SubmissionPdfRenderer.Render(Source(SimpleSchema, null, """{"nom":"FICTIF"}"""), out var rejected);

        Assert.False(rejected);
        Assert.Contains("<table class=\"values\">", pdf.Body);
    }
}

public class CseLogoTests
{
    [Fact]
    public void Png_and_jpeg_become_data_uris()
    {
        Assert.StartsWith("data:image/png;base64,", CseLogo.ToDataUri([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01]));
        Assert.StartsWith("data:image/jpeg;base64,", CseLogo.ToDataUri([0xFF, 0xD8, 0xFF, 0xE0, 0x01]));
    }

    [Fact]
    public void Other_or_oversized_files_are_refused()
    {
        Assert.Null(CseLogo.ToDataUri("<svg onload=\"x()\"></svg>"u8));
        Assert.Null(CseLogo.ToDataUri("GIF89a"u8));
        Assert.Null(CseLogo.ToDataUri([]));

        var big = new byte[CseLogo.MaxBytes + 1];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(big, 0);
        Assert.Null(CseLogo.ToDataUri(big));
    }

    [Theory]
    [InlineData("data:image/svg+xml;base64,PHN2Zz4=")]
    [InlineData("data:image/png;base64,AAA\" onerror=\"x")]
    [InlineData("https://example.test/logo.png")]
    [InlineData("")]
    public void Only_a_valid_data_uri_becomes_an_image(string value)
    {
        Assert.Null(CseLogo.ToImageTag(value));
        Assert.NotNull(SettingKeys.Validate(SettingKeys.CseLogo, value));
    }

    [Fact]
    public void Decoded_size_is_reported()
    {
        var uri = CseLogo.ToDataUri(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 })!;

        Assert.Equal(12, CseLogo.DecodedSize(uri));
    }
}
