using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using CseForms.Application.Pdf;
using CseForms.Application.Settings;

namespace CseForms.Application.Distribution;

/// <summary>Contenu d'un flyer. Le code d'accès n'est connu que le temps de produire le document.</summary>
public sealed record FlyerContent(
    string FormTitle,
    string Url,
    string QrSvg,
    string? Site,
    string? AccessCode,
    IReadOnlyDictionary<string, string> Settings);

/// <summary>
/// Flyer A4 à imprimer : logo et nom du CSE, titre, QR code, adresse lisible, code d'accès et contact du CSE.
/// Document autonome : aucune ressource externe ; tous les textes sont encodés.
/// </summary>
public static class FlyerRenderer
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    private const string Head = """
        <meta charset="utf-8">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:">
        """;

    private const string Style = """
        <style>
          @page { size: A4; }
          * { box-sizing: border-box; }
          body { margin: 0; font-family: "DejaVu Sans", "Liberation Sans", Arial, sans-serif; color: #1a1a1a; text-align: center; }
          .entete { display: flex; align-items: center; justify-content: center; gap: 14px; margin-top: 4mm; }
          .cse-logo { max-height: 22mm; max-width: 50mm; }
          .cse-nom { font-size: 16pt; font-weight: bold; color: #1f4e8c; }
          h1 { font-size: 28pt; line-height: 1.2; margin: 12mm 10mm 2mm; }
          .site { font-size: 18pt; color: #1f4e8c; margin: 0 0 4mm; }
          .qr { width: 95mm; height: 95mm; margin: 6mm auto 3mm; }
          .qr svg { width: 100%; height: 100%; display: block; }
          .consigne { font-size: 14pt; margin: 2mm 0; }
          .url { font-family: "DejaVu Sans Mono", "Liberation Mono", monospace; font-size: 15pt; font-weight: bold; word-break: break-all; margin: 2mm 12mm 8mm; }
          .code { display: inline-block; border: 3px solid #1f4e8c; border-radius: 8px; padding: 4mm 10mm; margin-bottom: 8mm; }
          .code span { display: block; font-size: 12pt; color: #444; }
          .code strong { display: block; font-family: "DejaVu Sans Mono", "Liberation Mono", monospace; font-size: 30pt; letter-spacing: 6px; margin-top: 1mm; }
          .rgpd { font-size: 10pt; color: #444; margin: 0 18mm 3mm; }
          .contact { font-size: 11pt; color: #1a1a1a; }
        </style>
        """;

    public static PdfHtml Render(FlyerContent content)
    {
        string? Setting(string key) =>
            content.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        var cseName = Setting(SettingKeys.CseName) ?? "CSE";
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"fr\"><head>").Append(Head)
            .Append("<title>").Append(Encode(content.FormTitle)).Append("</title>").Append(Style).Append("</head><body>");

        html.Append("<div class=\"entete\">").Append(CseLogo.ToImageTag(Setting(SettingKeys.CseLogo)) ?? string.Empty)
            .Append("<div class=\"cse-nom\">").Append(Encode(cseName)).Append("</div></div>");

        html.Append("<h1>").Append(Encode(content.FormTitle)).Append("</h1>");
        if (content.Site is not null)
        {
            html.Append("<p class=\"site\">Site : ").Append(Encode(content.Site)).Append("</p>");
        }

        // SVG produit par le générateur de QR code à partir de l'adresse : jamais une saisie.
        html.Append("<div class=\"qr\">").Append(content.QrSvg).Append("</div>");
        html.Append("<p class=\"consigne\">Scannez ce code avec l'appareil photo de votre téléphone,<br>ou rendez-vous sur :</p>");
        html.Append("<p class=\"url\">").Append(Encode(Readable(content.Url))).Append("</p>");

        if (content.AccessCode is not null)
        {
            html.Append("<div class=\"code\"><span>Code d'accès</span><strong>").Append(Encode(content.AccessCode)).Append("</strong></div>");
        }

        html.Append("<p class=\"rgpd\">Vos réponses sont consultées uniquement par les membres habilités du CSE et ne sont jamais transmises à l'employeur. Le formulaire vous informe sur l'usage de vos données et sur vos droits.</p>");
        var contact = new[] { Setting(SettingKeys.CseEmail), Setting(SettingKeys.CsePhone) }.Where(c => c is not null).ToList();
        if (contact.Count > 0)
        {
            html.Append("<p class=\"contact\">Contact : ").Append(Encode(string.Join(" · ", contact))).Append("</p>");
        }

        html.Append("</body></html>");

        var footer = $$"""
            <!doctype html><html><head>{{Head}}<style>
              body { margin: 0; width: 100%; font-family: "DejaVu Sans", "Liberation Sans", Arial, sans-serif; font-size: 7.5pt; color: #888; text-align: center; }
            </style></head><body><div>{{Encode(cseName)}}</div></body></html>
            """;

        return new PdfHtml(html.ToString(), footer);
    }

    /// <summary>Adresse affichée sans « https:// » ni paramètre de site encodé illisible.</summary>
    private static string Readable(string url)
    {
        var text = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url["https://".Length..] : url;
        return Uri.UnescapeDataString(text);
    }

    private static string Encode(string value) => Encoder.Encode(value);
}
