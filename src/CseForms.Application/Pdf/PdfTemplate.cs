using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.FormDefinitions;
using CseForms.Application.Settings;

namespace CseForms.Application.Pdf;

/// <summary>
/// Modèle PDF propre à un formulaire (<c>form_version.pdf_template_html</c>) : un fragment HTML avec
/// une syntaxe minimale, contrôlé à la publication contre la définition de la même version.
/// <list type="bullet">
/// <item><c>{{ nom }}</c> : valeur encodée, jamais interprétée comme du HTML ;</item>
/// <item><c>{{#if nom}} ... {{else}} ... {{/if}}</c> : bloc conditionnel (valeur présente) ;</item>
/// <item><c>{{#each field.groupe}} ... {{/each}}</c> : une fois par ligne d'un groupe répétable, avec <c>item.champ</c> et <c>item.index</c>.</item>
/// </list>
/// Le modèle ne peut charger aucune ressource : ni script, ni lien, ni image autre que le logo des paramètres.
/// </summary>
public sealed partial class PdfTemplate
{
    public const int MaxLength = 100_000;

    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>Valeurs générales, hors champs du formulaire.</summary>
    public static readonly IReadOnlyList<string> GeneralNames =
    [
        "form.title", "form.version", "case.number", "submitted.date", "submitted.time",
        "cse.name", "cse.address", "cse.email", "cse.phone", "cse.office", "cse.logo",
    ];

    private static readonly Dictionary<string, string> SettingNames = new(StringComparer.Ordinal)
    {
        ["cse.name"] = SettingKeys.CseName,
        ["cse.address"] = SettingKeys.CseAddress,
        ["cse.email"] = SettingKeys.CseEmail,
        ["cse.phone"] = SettingKeys.CsePhone,
        ["cse.office"] = SettingKeys.CseOffice,
    };

    // Balises et attributs qui chargeraient une ressource, exécuteraient du code ou sortiraient du fragment.
    private static readonly (Regex Pattern, string Message)[] ForbiddenMarkup =
    [
        (ForbiddenTagPattern(), "Balise interdite : seuls la mise en forme et le logo des paramètres sont permis."),
        (EventAttributePattern(), "Attribut d'événement interdit."),
        (ResourceAttributePattern(), "Attribut de ressource interdit (src, href...) : le modèle ne charge rien."),
        (CssResourcePattern(), "Ressource CSS interdite (url, @import) : le modèle ne charge rien."),
        (ScriptUrlPattern(), "Adresse javascript: interdite."),
    ];

    private readonly IReadOnlyList<Node> _nodes;

    private PdfTemplate(IReadOnlyList<Node> nodes) => _nodes = nodes;

    /// <summary>Contrôle le modèle contre la définition ; les erreurs donnent la ligne concernée.</summary>
    public static PdfTemplateParseResult Parse(string template, FormDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return Fail("Le modèle est vide.");
        }

        if (template.Length > MaxLength)
        {
            return Fail($"Le modèle dépasse {MaxLength} caractères.");
        }

        var errors = new List<string>();
        foreach (var (pattern, message) in ForbiddenMarkup)
        {
            foreach (Match match in pattern.Matches(template))
            {
                errors.Add($"Ligne {LineOf(template, match.Index)} : {message}");
            }
        }

        var parser = new Parser(template, Scope.Create(definition), errors);
        var nodes = parser.ParseAll();
        return errors.Count > 0 ? new PdfTemplateParseResult(null, errors) : new PdfTemplateParseResult(new PdfTemplate(nodes), []);
    }

    /// <summary>Produit le fragment HTML de la réponse. Les champs absents (masqués à la saisie) sont vides.</summary>
    public string Render(PdfSource source, FormDefinition definition, JsonObject data)
    {
        var context = new RenderContext(source, Scope.Create(definition), data);
        var html = new StringBuilder();
        RenderNodes(_nodes, context, item: null, html);
        return html.ToString();
    }

    private static void RenderNodes(IReadOnlyList<Node> nodes, RenderContext context, RowContext? item, StringBuilder html)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    html.Append(text.Html);
                    break;
                case ValueNode value when value.Name == "cse.logo":
                    if (CseLogo.ToImageTag(context.Setting(SettingKeys.CseLogo)) is { } img)
                    {
                        html.Append(img);
                    }

                    break;
                case ValueNode value:
                    html.Append(Encoder.Encode(context.Text(value.Name, item) ?? string.Empty));
                    break;
                case IfNode condition:
                    RenderNodes(context.IsPresent(condition.Name, item) ? condition.Then : condition.Else, context, item, html);
                    break;
                case EachNode each:
                    var rows = context.Rows(each.Name);
                    for (var i = 0; i < rows.Count; i++)
                    {
                        RenderNodes(each.Body, context, new RowContext(context.Scope.Repeats[each.Name], rows[i], i + 1), html);
                    }

                    break;
            }
        }
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static PdfTemplateParseResult Fail(string message) => new(null, [message]);

    [GeneratedRegex(@"\{\{(.*?)\}\}", RegexOptions.Singleline)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^\s*(?:(?<kw>#if|#each)\s+(?<name>[A-Za-z0-9_.]+)|(?<kw>else|/if|/each)|(?<name>[A-Za-z0-9_.]+))\s*$")]
    private static partial Regex TagContentPattern();

    [GeneratedRegex(@"<\s*/?\s*(script|iframe|frame|frameset|object|embed|applet|link|meta|base|form|input|button|img|image|svg|math|video|audio|source|track|picture|portal|html|head|body|title|template)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenTagPattern();

    [GeneratedRegex(@"<[^>]*\son[a-z]+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex EventAttributePattern();

    [GeneratedRegex(@"<[^>]*\s(src|srcset|href|xlink:href|action|formaction|poster|background|data)\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex ResourceAttributePattern();

    [GeneratedRegex(@"url\s*\(|@import|image-set\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex CssResourcePattern();

    [GeneratedRegex(@"javascript\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptUrlPattern();

    /// <summary>Noms utilisables, déduits de la définition de la version.</summary>
    private sealed record Scope(
        IReadOnlyDictionary<string, FieldDefinition> Fields,
        IReadOnlyDictionary<string, FieldDefinition> Repeats)
    {
        public static Scope Create(FormDefinition definition)
        {
            var fields = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal);
            var repeats = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal);
            foreach (var field in definition.AllTopLevelFields.Where(f => f.Type != FieldType.Info))
            {
                fields[field.Id] = field;
                if (field.Type == FieldType.Repeat)
                {
                    repeats[$"field.{field.Id}"] = field;
                }
            }

            return new Scope(fields, repeats);
        }
    }

    private enum NameUse
    {
        Value,
        Condition,
    }

    private sealed class Parser(string template, Scope scope, List<string> errors)
    {
        private readonly MatchCollection _tags = TagPattern().Matches(template);
        private int _tagIndex;
        private int _textStart;
        private FieldDefinition? _currentRepeat;

        public IReadOnlyList<Node> ParseAll()
        {
            var nodes = new List<Node>();
            while (true)
            {
                nodes.AddRange(ParseUntil(out var end, out var endLine));
                if (end is null)
                {
                    break;
                }

                errors.Add($"Ligne {endLine} : {{{{{end}}}}} sans bloc ouvert.");
            }

            AppendText(nodes, template.Length);
            CheckStrayBraces();
            return nodes;
        }

        private List<Node> ParseUntil(out string? endKeyword, out int endLine)
        {
            var nodes = new List<Node>();
            endKeyword = null;
            endLine = 0;

            while (_tagIndex < _tags.Count)
            {
                var tag = _tags[_tagIndex++];
                AppendText(nodes, tag.Index);
                _textStart = tag.Index + tag.Length;
                var line = LineOf(template, tag.Index);

                var content = TagContentPattern().Match(tag.Groups[1].Value);
                if (!content.Success)
                {
                    errors.Add($"Ligne {line} : balise de modèle incorrecte.");
                    continue;
                }

                var keyword = content.Groups["kw"].Success ? content.Groups["kw"].Value : null;
                var name = content.Groups["name"].Value;
                switch (keyword)
                {
                    case null:
                        if (CheckName(name, NameUse.Value, line))
                        {
                            nodes.Add(new ValueNode(name));
                        }

                        break;
                    case "#if":
                        nodes.Add(ParseIf(name, line));
                        break;
                    case "#each":
                        nodes.Add(ParseEach(name, line));
                        break;
                    default:
                        endKeyword = keyword;
                        endLine = line;
                        return nodes;
                }
            }

            return nodes;
        }

        private IfNode ParseIf(string name, int line)
        {
            CheckName(name, NameUse.Condition, line);
            var then = ParseUntil(out var end, out var endLine);
            IReadOnlyList<Node> otherwise = [];
            if (end == "else")
            {
                otherwise = ParseUntil(out end, out endLine);
            }

            if (end != "/if")
            {
                errors.Add(end is null
                    ? $"Ligne {line} : {{{{#if}}}} non fermé par {{{{/if}}}}."
                    : $"Ligne {endLine} : {{{{{end}}}}} inattendu, {{{{/if}}}} attendu.");
            }

            return new IfNode(name, then, otherwise);
        }

        private EachNode ParseEach(string name, int line)
        {
            if (_currentRepeat is not null)
            {
                errors.Add($"Ligne {line} : {{{{#each}}}} imbriqués non permis.");
            }
            else if (!scope.Repeats.ContainsKey(name))
            {
                errors.Add($"Ligne {line} : « {name} » n'est pas un groupe répétable du formulaire.");
            }

            var outer = _currentRepeat;
            _currentRepeat = scope.Repeats.GetValueOrDefault(name) ?? outer;
            var body = ParseUntil(out var end, out var endLine);
            _currentRepeat = outer;

            if (end != "/each")
            {
                errors.Add(end is null
                    ? $"Ligne {line} : {{{{#each}}}} non fermé par {{{{/each}}}}."
                    : $"Ligne {endLine} : {{{{{end}}}}} inattendu, {{{{/each}}}} attendu.");
            }

            return new EachNode(name, body);
        }

        private bool CheckName(string name, NameUse use, int line)
        {
            string? problem = null;
            if (GeneralNames.Contains(name))
            {
                // Valeur générale : toujours connue.
            }
            else if (name.StartsWith("field.", StringComparison.Ordinal))
            {
                var id = name["field.".Length..];
                if (!scope.Fields.TryGetValue(id, out var field))
                {
                    problem = $"champ « {id} » absent de la définition.";
                }
                else if (field.Type == FieldType.Repeat && use == NameUse.Value)
                {
                    problem = $"le groupe « {id} » s'affiche avec {{{{#each {name}}}}}.";
                }
            }
            else if (name.StartsWith("label.", StringComparison.Ordinal))
            {
                if (use == NameUse.Condition || !scope.Fields.ContainsKey(name["label.".Length..]))
                {
                    problem = $"libellé « {name} » inconnu.";
                }
            }
            else if (name.StartsWith("item.", StringComparison.Ordinal))
            {
                var id = name["item.".Length..];
                if (_currentRepeat is null)
                {
                    problem = $"« {name} » n'est utilisable que dans {{{{#each}}}}.";
                }
                else if (id != "index" && _currentRepeat.Fields!.All(f => f.Id != id || f.Type == FieldType.Info))
                {
                    problem = $"champ « {id} » absent du groupe « {_currentRepeat.Id} ».";
                }
            }
            else
            {
                problem = $"nom « {name} » inconnu.";
            }

            if (problem is not null)
            {
                errors.Add($"Ligne {line} : {problem}");
                return false;
            }

            return true;
        }

        private void AppendText(List<Node> nodes, int end)
        {
            if (end > _textStart)
            {
                nodes.Add(new TextNode(template[_textStart..end]));
            }
        }

        private void CheckStrayBraces()
        {
            // Une accolade double restante signale une balise mal fermée.
            var withoutTags = TagPattern().Replace(template, match => new string(' ', match.Length));
            var index = withoutTags.IndexOf("{{", StringComparison.Ordinal);
            if (index < 0)
            {
                index = withoutTags.IndexOf("}}", StringComparison.Ordinal);
            }

            if (index >= 0)
            {
                errors.Add($"Ligne {LineOf(template, index)} : balise de modèle non fermée.");
            }
        }
    }

    private sealed class RenderContext(PdfSource source, Scope scope, JsonObject data)
    {
        public Scope Scope => scope;

        public string? Setting(string key) =>
            source.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        public string? Text(string name, RowContext? item)
        {
            if (name.StartsWith("item.", StringComparison.Ordinal))
            {
                return item?.Text(name["item.".Length..]);
            }

            if (name.StartsWith("label.", StringComparison.Ordinal))
            {
                var field = scope.Fields[name["label.".Length..]];
                return field.Label ?? field.Id;
            }

            if (name.StartsWith("field.", StringComparison.Ordinal))
            {
                var field = scope.Fields[name["field.".Length..]];
                return data.TryGetPropertyValue(field.Id, out var value) && value is not null
                    ? SubmissionPresenter.Format(field, value)
                    : null;
            }

            if (SettingNames.TryGetValue(name, out var key))
            {
                return Setting(key);
            }

            var local = CseCalendar.ToLocal(source.SubmittedAt);
            return name switch
            {
                "form.title" => source.FormTitle,
                "form.version" => source.VersionNumber.ToString(CultureInfo.InvariantCulture),
                "case.number" => source.CaseNumber,
                "submitted.date" => local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                "submitted.time" => local.ToString("HH:mm", CultureInfo.InvariantCulture),
                _ => null,
            };
        }

        public bool IsPresent(string name, RowContext? item)
        {
            if (name == "cse.logo")
            {
                return CseLogo.ToImageTag(Setting(SettingKeys.CseLogo)) is not null;
            }

            if (name.StartsWith("item.", StringComparison.Ordinal))
            {
                return item?.IsPresent(name["item.".Length..]) ?? false;
            }

            if (name.StartsWith("field.", StringComparison.Ordinal))
            {
                var field = scope.Fields[name["field.".Length..]];
                return data.TryGetPropertyValue(field.Id, out var value) && IsFilled(field, value);
            }

            return !string.IsNullOrWhiteSpace(Text(name, item));
        }

        public IReadOnlyList<JsonObject> Rows(string name) =>
            data.TryGetPropertyValue(scope.Repeats[name].Id, out var value) && value is JsonArray rows
                ? rows.OfType<JsonObject>().ToList()
                : [];
    }

    private sealed class RowContext(FieldDefinition repeat, JsonObject row, int index)
    {
        public string? Text(string id)
        {
            if (id == "index")
            {
                return index.ToString(CultureInfo.InvariantCulture);
            }

            var field = repeat.Fields!.First(f => f.Id == id);
            return row.TryGetPropertyValue(id, out var value) && value is not null ? SubmissionPresenter.Format(field, value) : null;
        }

        public bool IsPresent(string id) =>
            id == "index" || (row.TryGetPropertyValue(id, out var value) && IsFilled(repeat.Fields!.First(f => f.Id == id), value));
    }

    /// <summary>Une case non cochée, une liste vide ou un texte vide comptent comme absents.</summary>
    private static bool IsFilled(FieldDefinition field, JsonNode? value) => value switch
    {
        null => false,
        JsonArray array => array.Count > 0,
        JsonValue v when field.Type == FieldType.Consent => v.TryGetValue<bool>(out var accepted) && accepted,
        _ => !string.IsNullOrWhiteSpace(SubmissionPresenter.Format(field, value)),
    };

    private abstract record Node;

    private sealed record TextNode(string Html) : Node;

    private sealed record ValueNode(string Name) : Node;

    private sealed record IfNode(string Name, IReadOnlyList<Node> Then, IReadOnlyList<Node> Else) : Node;

    private sealed record EachNode(string Name, IReadOnlyList<Node> Body) : Node;
}

public sealed record PdfTemplateParseResult(PdfTemplate? Template, IReadOnlyList<string> Errors)
{
    public bool IsValid => Template is not null;
}
