using System.Globalization;
using System.Text;
using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.FormDefinitions;
using CseForms.Application.Settings;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace CseForms.Application.Pdf;

/// <summary>
/// Modèle PDF générique : en-tête du CSE, numéro de dossier, date de soumission, sections de la version
/// remplie (libellé et valeur, groupes répétables en tableau) et composition du bureau.
/// Chaque texte est encodé : aucune valeur saisie n'est interprétée comme du HTML.
/// </summary>
public static class SubmissionPdfRenderer
{
    // Encode les caractères spéciaux du HTML, sans transformer les lettres accentuées.
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    // Le document est autonome : ni script, ni ressource externe.
    private const string Head = """
        <meta charset="utf-8">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
        """;

    private const string Style = """
        <style>
          @page { size: A4; }
          * { box-sizing: border-box; }
          body { margin: 0; font-family: "DejaVu Sans", "Liberation Sans", Arial, sans-serif; font-size: 10pt; color: #1a1a1a; line-height: 1.4; }
          header { display: flex; justify-content: space-between; gap: 24px; border-bottom: 2px solid #1f4e8c; padding-bottom: 10px; }
          .cse-name { font-size: 14pt; font-weight: bold; color: #1f4e8c; }
          .contact { text-align: right; font-size: 9pt; color: #444; white-space: pre-line; }
          h1 { font-size: 16pt; margin: 18px 0 8px; }
          .meta { display: flex; flex-wrap: wrap; gap: 6px 24px; background: #eef2f8; border-radius: 4px; padding: 8px 12px; margin-bottom: 14px; }
          .meta div { min-width: 40%; }
          .meta span { color: #555; }
          .online { font-weight: bold; color: #1f4e8c; }
          section { break-inside: avoid; margin-bottom: 12px; }
          h2 { font-size: 11.5pt; color: #1f4e8c; border-bottom: 1px solid #c9d3e3; padding-bottom: 2px; margin: 0 0 6px; }
          h3 { font-size: 10pt; margin: 8px 0 4px; }
          table { width: 100%; border-collapse: collapse; }
          table.values th { width: 38%; text-align: left; font-weight: normal; color: #555; vertical-align: top; padding: 3px 8px 3px 0; }
          table.values td { padding: 3px 0; vertical-align: top; white-space: pre-line; }
          table.grid th, table.grid td { border: 1px solid #c9d3e3; padding: 4px 6px; text-align: left; }
          table.grid th { background: #eef2f8; }
          .empty { color: #777; font-style: italic; }
          .office { margin-top: 18px; font-size: 9pt; color: #444; white-space: pre-line; }
        </style>
        """;

    public static PdfHtml Render(PdfSource source)
    {
        var parsed = FormDefinitionParser.Parse(source.SchemaJson);
        var sections = parsed.IsValid
            ? SubmissionPresenter.Present(parsed.Definition!, JsonData.ParseObject(source.DataJson))
            : [];

        var settings = source.Settings;
        string? Setting(string key) => settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        var local = CseCalendar.ToLocal(source.SubmittedAt);
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"fr\"><head>").Append(Head)
            .Append("<title>").Append(HtmlEncode(source.CaseNumber)).Append("</title>").Append(Style).Append("</head><body>");

        html.Append("<header><div class=\"cse-name\">").Append(HtmlEncode(Setting(SettingKeys.CseName) ?? "CSE")).Append("</div>");
        var contact = new[] { Setting(SettingKeys.CseAddress), Setting(SettingKeys.CseEmail), Setting(SettingKeys.CsePhone) }
            .Where(line => line is not null);
        html.Append("<div class=\"contact\">").Append(HtmlEncode(string.Join('\n', contact))).Append("</div></header>");

        html.Append("<h1>").Append(HtmlEncode(source.FormTitle)).Append("</h1>");
        html.Append("<div class=\"meta\">")
            .Append("<div><span>Numéro de dossier : </span><strong>").Append(HtmlEncode(source.CaseNumber)).Append("</strong></div>")
            .Append("<div><span>Soumis le : </span>")
            .Append(local.ToString("dd/MM/yyyy 'à' HH:mm", CultureInfo.InvariantCulture)).Append("</div>")
            .Append("<div class=\"online\">Formulaire soumis en ligne</div>")
            .Append("<div><span>Version du formulaire : </span>").Append(source.VersionNumber.ToString(CultureInfo.InvariantCulture)).Append("</div>")
            .Append("</div>");

        foreach (var section in sections)
        {
            AppendSection(html, section);
        }

        if (Setting(SettingKeys.CseOffice) is { } office)
        {
            html.Append("<div class=\"office\"><strong>Bureau du CSE</strong>\n").Append(HtmlEncode(office)).Append("</div>");
        }

        html.Append("</body></html>");

        return new PdfHtml(html.ToString(), Footer(source.CaseNumber));
    }

    private static void AppendSection(StringBuilder html, PresentedSection section)
    {
        html.Append("<section>");
        if (section.Title is not null)
        {
            html.Append("<h2>").Append(HtmlEncode(section.Title)).Append("</h2>");
        }

        var values = new List<PresentedValue>();
        foreach (var item in section.Items)
        {
            if (item is PresentedValue value)
            {
                values.Add(value);
                continue;
            }

            FlushValues(html, values);
            if (item is PresentedTable table)
            {
                AppendTable(html, table);
            }
        }

        FlushValues(html, values);
        html.Append("</section>");
    }

    private static void FlushValues(StringBuilder html, List<PresentedValue> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        html.Append("<table class=\"values\">");
        foreach (var value in values)
        {
            html.Append("<tr><th scope=\"row\">").Append(HtmlEncode(value.Label)).Append("</th><td>")
                .Append(HtmlEncode(value.Value)).Append("</td></tr>");
        }

        html.Append("</table>");
        values.Clear();
    }

    private static void AppendTable(StringBuilder html, PresentedTable table)
    {
        html.Append("<h3>").Append(HtmlEncode(table.Label)).Append("</h3>");
        if (table.Rows.Count == 0)
        {
            html.Append("<p class=\"empty\">Aucun</p>");
            return;
        }

        html.Append("<table class=\"grid\"><thead><tr>");
        foreach (var header in table.Headers)
        {
            html.Append("<th scope=\"col\">").Append(HtmlEncode(header)).Append("</th>");
        }

        html.Append("</tr></thead><tbody>");
        foreach (var row in table.Rows)
        {
            html.Append("<tr>");
            foreach (var cell in row)
            {
                html.Append("<td>").Append(HtmlEncode(cell)).Append("</td>");
            }

            html.Append("</tr>");
        }

        html.Append("</tbody></table>");
    }

    private static string HtmlEncode(string value) => Encoder.Encode(value);

    /// <summary>Pied de page de Chromium : les classes pageNumber et totalPages sont remplies à l'impression.</summary>
    private static string Footer(string caseNumber) => $$"""
        <!doctype html><html><head>{{Head}}<style>
          body { margin: 0; width: 100%; font-family: "DejaVu Sans", "Liberation Sans", Arial, sans-serif; font-size: 7.5pt; color: #666; }
          div { box-sizing: border-box; display: flex; justify-content: space-between; width: 100%; padding: 0 15mm; }
        </style></head><body><div>
          <span>Document confidentiel du CSE · Dossier {{HtmlEncode(caseNumber)}}</span>
          <span>Page <span class="pageNumber"></span> / <span class="totalPages"></span></span>
        </div></body></html>
        """;
}
