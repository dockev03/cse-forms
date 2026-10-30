using System.Globalization;
using System.Text.Json.Nodes;
using CseForms.Application.FormDefinitions;

namespace CseForms.Application.BackOffice;

/// <summary>
/// Met en forme une réponse avec les libellés de la version remplie. Les champs absents
/// (masqués lors de la saisie) ne sont pas affichés.
/// </summary>
public static class SubmissionPresenter
{
    public static IReadOnlyList<PresentedSection> Present(FormDefinition definition, JsonObject data)
    {
        var sections = new List<PresentedSection>();

        foreach (var section in definition.Sections)
        {
            var items = new List<PresentedItem>();
            foreach (var field in section.Fields)
            {
                if (field.Type == FieldType.Info || !data.TryGetPropertyValue(field.Id, out var value) || value is null)
                {
                    continue;
                }

                items.Add(field.Type == FieldType.Repeat
                    ? PresentTable(field, value)
                    : new PresentedValue(field.Label ?? field.Id, Format(field, value)));
            }

            if (items.Count > 0)
            {
                sections.Add(new PresentedSection(section.Title, items));
            }
        }

        return sections;
    }

    private static PresentedTable PresentTable(FieldDefinition field, JsonNode value)
    {
        var columns = field.Fields!.Where(f => f.Type != FieldType.Info).ToList();
        var rows = (value as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(row => (IReadOnlyList<string>)columns
                .Select(c => row.TryGetPropertyValue(c.Id, out var cell) && cell is not null ? Format(c, cell) : string.Empty)
                .ToList())
            .ToList();

        return new PresentedTable(field.Label ?? field.Id, columns.Select(c => c.Label ?? c.Id).ToList(), rows);
    }

    public static string Format(FieldDefinition field, JsonNode value) => field.Type switch
    {
        FieldType.Date => FormatDate(value.GetValue<string>()),
        FieldType.Consent => value.GetValue<bool>() ? "Oui" : "Non",
        FieldType.Multichoice => string.Join(", ", (value as JsonArray ?? []).Select(v => v?.GetValue<string>())),
        FieldType.Address => FormatAddress(value as JsonObject),
        _ => value is JsonValue v && v.TryGetValue<string>(out var text) ? text : value.ToJsonString(),
    };

    public static string FormatDate(string isoDate) =>
        DateOnly.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : isoDate;

    private static string FormatAddress(JsonObject? address)
    {
        if (address is null)
        {
            return string.Empty;
        }

        string Part(string name) => address[name]?.GetValue<string>() ?? string.Empty;
        return $"{Part("street")}, {Part("postalCode")} {Part("city")}".Trim(' ', ',');
    }
}
