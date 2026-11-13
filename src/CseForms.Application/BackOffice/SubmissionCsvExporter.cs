using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CseForms.Application.FormDefinitions;

namespace CseForms.Application.BackOffice;

/// <summary>
/// Export CSV des réponses, lisible par Excel en français : séparateur point-virgule, UTF-8 avec BOM,
/// dates JJ/MM/AAAA. Une colonne par champ ; les champs des anciennes versions absents de la plus récente
/// sont ajoutés à la fin. Groupes répétables : une colonne par sous-champ et par ligne (« Enfants 1 - Nom »).
/// Adresses : voie, code postal et ville en colonnes séparées, pour l'import dans le logiciel du CSE.
/// </summary>
public static class SubmissionCsvExporter
{
    private const char Separator = ';';

    private static readonly string[] FixedHeaders = ["Dossier", "Reçue le", "Statut", "Version"];

    private static readonly (string Key, string Suffix)[] AddressParts =
        [("street", "voie"), ("postalCode", "code postal"), ("city", "ville")];

    public static byte[] Write(ExportData data)
    {
        var definitions = data.SchemasByVersion
            .Select(v => (Version: v.Key, Parsed: FormDefinitionParser.Parse(v.Value)))
            .Where(v => v.Parsed.IsValid)
            .ToDictionary(v => v.Version, v => v.Parsed.Definition!);

        var rows = data.Rows.Select(r => (Row: r, Data: JsonData.ParseObject(r.DataJson))).ToList();
        var columns = BuildColumns(definitions, rows.Select(r => r.Data).ToList());

        var csv = new StringBuilder();
        AppendLine(csv, FixedHeaders.Concat(columns.Select(c => c.Header)));

        foreach (var (row, values) in rows)
        {
            var fields = definitions.TryGetValue(row.VersionNumber, out var definition)
                ? definition.AllTopLevelFields.ToDictionary(f => f.Id)
                : [];

            var cells = new List<string>
            {
                row.CaseNumber,
                Display(row.CreatedAt),
                SubmissionStatuses.Label(row.Status),
                row.VersionNumber.ToString(CultureInfo.InvariantCulture),
            };
            cells.AddRange(columns.Select(c => c.Read(values, fields.GetValueOrDefault(c.FieldId))));
            AppendLine(csv, cells);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    /// <summary>
    /// Neutralise les formules (=, +, -, @, tabulation, retour chariot en tête) puis échappe pour le CSV.
    /// Un téléphone « +33... » devient « '+33... » : le prix d'une ouverture sans risque dans un tableur.
    /// </summary>
    public static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([Separator, '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private sealed record Column(string FieldId, string Header, Func<JsonObject, FieldDefinition?, string> Read);

    private static List<Column> BuildColumns(IReadOnlyDictionary<int, FormDefinition> definitions, IReadOnlyList<JsonObject> rows)
    {
        // Champ retenu pour l'en-tête : celui de la version la plus récente qui le contient.
        var fields = new List<FieldDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in definitions.OrderByDescending(d => d.Key).Select(d => d.Value))
        {
            foreach (var field in definition.AllTopLevelFields)
            {
                if (field.Type != FieldType.Info && field.Persist && seen.Add(field.Id))
                {
                    fields.Add(field);
                }
            }
        }

        var columns = new List<Column>();
        foreach (var field in fields)
        {
            var label = field.Label ?? field.Id;
            switch (field.Type)
            {
                case FieldType.Repeat:
                    var count = rows.Select(r => (r[field.Id] as JsonArray)?.Count ?? 0).DefaultIfEmpty(0).Max();
                    var subFields = (field.Fields ?? []).Where(f => f.Type != FieldType.Info).ToList();
                    for (var index = 0; index < count; index++)
                    {
                        foreach (var sub in subFields)
                        {
                            var i = index;
                            columns.Add(new Column(field.Id, $"{label} {i + 1} - {sub.Label ?? sub.Id}", (data, own) =>
                                RepeatCell(data, field.Id, i, own?.Fields?.FirstOrDefault(f => f.Id == sub.Id) ?? sub)));
                        }
                    }

                    break;
                case FieldType.Address:
                    foreach (var (key, suffix) in AddressParts)
                    {
                        columns.Add(new Column(field.Id, $"{label} ({suffix})", (data, _) =>
                            (data[field.Id] as JsonObject)?[key] is JsonValue part && part.TryGetValue<string>(out var text) ? text : string.Empty));
                    }

                    break;
                default:
                    columns.Add(new Column(field.Id, label, (data, own) => Cell(own ?? field, data[field.Id])));
                    break;
            }
        }

        return columns;
    }

    private static string RepeatCell(JsonObject data, string fieldId, int index, FieldDefinition sub) =>
        data[fieldId] is JsonArray items && index < items.Count && items[index] is JsonObject item
            ? Cell(sub, item[sub.Id])
            : string.Empty;

    /// <summary>Valeur mise en forme comme sur la fiche ; une valeur inattendue est exportée telle quelle.</summary>
    private static string Cell(FieldDefinition field, JsonNode? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        try
        {
            return SubmissionPresenter.Format(field, value);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return value is JsonValue v && v.TryGetValue<string>(out var text) ? text : value.ToJsonString();
        }
    }

    private static string Display(DateTimeOffset utc) =>
        Common.CseCalendar.ToLocal(utc).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static void AppendLine(StringBuilder csv, IEnumerable<string> cells)
    {
        csv.AppendJoin(Separator, cells.Select(Escape));
        csv.Append("\r\n");
    }
}
