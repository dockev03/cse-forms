using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CseForms.Application.FormDefinitions;

/// <summary>
/// Validation serveur d'une réponse, à partir de la définition publiée. Elle fait foi :
/// un champ masqué par une condition n'est ni validé ni enregistré, un champ inconnu est ignoré.
/// </summary>
public static partial class SubmissionValidator
{
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    public static SubmissionValidationResult Validate(FormDefinition definition, JsonElement input, ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);

        if (input.ValueKind != JsonValueKind.Object)
        {
            return new SubmissionValidationResult(
                [new ValidationError(string.Empty, ValidationCodes.InvalidType, "Réponse illisible.")],
                null);
        }

        return new Run(definition, input, context).Execute();
    }

    private sealed class Run
    {
        private readonly ValidationContext _context;
        private readonly Scope _top;
        private readonly List<ValidationError> _errors = [];
        private readonly List<(Scope Scope, FieldDefinition Field, string Path)> _pendingRules = [];
        private readonly FormDefinition _definition;
        private readonly FormPath _path;

        public Run(FormDefinition definition, JsonElement input, ValidationContext context)
        {
            _definition = definition;
            _context = context;

            var sectionOf = new Dictionary<string, SectionDefinition>(StringComparer.Ordinal);
            foreach (var section in definition.Sections)
            {
                foreach (var field in section.Fields)
                {
                    sectionOf[field.Id] = section;
                }
            }

            _top = new Scope(
                definition.AllTopLevelFields.ToDictionary(f => f.Id, StringComparer.Ordinal),
                input,
                parent: null);
            _path = new FormPath(definition, _top);
            _top.VisibilityRule = field =>
                _path.IsSectionShown(sectionOf[field.Id]) && (field.VisibleIf is null || Evaluate(field.VisibleIf, _top));
        }

        public SubmissionValidationResult Execute()
        {
            var output = new JsonObject();

            foreach (var field in _definition.AllTopLevelFields)
            {
                ProcessField(field, _top, field.Id, output);
            }

            foreach (var (scope, field, path) in _pendingRules)
            {
                ApplyRules(field, scope, path);
            }

            return new SubmissionValidationResult(_errors, _errors.Count == 0 ? output : null, _path.Ending);
        }

        private void ProcessField(FieldDefinition field, Scope scope, string path, JsonObject output)
        {
            if (!scope.IsVisible(field.Id))
            {
                return;
            }

            var node = ValidateField(field, scope.Raw(field.Id), path);
            if (node is null)
            {
                return;
            }

            scope.Normalized[field.Id] = node;
            if (field.Rules is { Count: > 0 })
            {
                _pendingRules.Add((scope, field, path));
            }

            if (field.Persist)
            {
                output[field.Id] = node.DeepClone();
            }
        }

        private JsonNode? ValidateField(FieldDefinition field, JsonElement? raw, string path)
        {
            var label = field.Label ?? field.Id;
            var present = raw is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) };

            switch (field.Type)
            {
                case FieldType.Info:
                    return null;

                case FieldType.Consent:
                    return ValidateConsent(field, raw, present, path, label);

                case FieldType.Multichoice:
                    return ValidateMultichoice(field, raw, present, path, label);

                case FieldType.Address:
                    return ValidateAddress(field, raw, present, path, label);

                case FieldType.Repeat:
                    return ValidateRepeat(field, raw, present, path, label);
            }

            if (present && raw!.Value.ValueKind != JsonValueKind.String)
            {
                Error(path, ValidationCodes.InvalidType, label);
                return null;
            }

            var text = present ? raw!.Value.GetString()! : string.Empty;
            text = field.Type == FieldType.Textarea ? TextNormalizer.MultiLine(text) : TextNormalizer.SingleLine(text);
            text = TextNormalizer.Apply(field.Transform, text);

            if (text.Length == 0)
            {
                if (field.Required)
                {
                    Error(path, ValidationCodes.Required, label);
                }

                return null;
            }

            var value = ValidateScalar(field, text, path, label);
            return value is null ? null : JsonValue.Create(value);
        }

        private string? ValidateScalar(FieldDefinition field, string text, string path, string label)
        {
            var maxLength = field.MaxLength ?? FieldLimits.DefaultMaxLength(field.Type);
            if (text.Length > maxLength)
            {
                Error(path, ValidationCodes.TooLong, label, maxLength.ToString(CultureInfo.InvariantCulture));
                return null;
            }

            switch (field.Type)
            {
                case FieldType.Text or FieldType.Textarea:
                    if (field.MinLength is { } min && text.Length < min)
                    {
                        Error(path, ValidationCodes.TooShort, label, min.ToString(CultureInfo.InvariantCulture));
                        return null;
                    }

                    if (field.Pattern is not null && !Matches(field.Pattern, text))
                    {
                        Error(path, ValidationCodes.InvalidFormat, label);
                        return null;
                    }

                    return text;

                case FieldType.Email:
                    var email = text.ToLowerInvariant();
                    if (!EmailPattern().IsMatch(email))
                    {
                        Error(path, ValidationCodes.InvalidEmail, label);
                        return null;
                    }

                    return email;

                case FieldType.Phone:
                    var phone = PhoneSeparators().Replace(text, string.Empty);
                    if (!PhonePattern().IsMatch(phone))
                    {
                        Error(path, ValidationCodes.InvalidPhone, label);
                        return null;
                    }

                    return phone;

                case FieldType.Date:
                    if (!DateExpression.TryParseDate(text, out _))
                    {
                        Error(path, ValidationCodes.InvalidDate, label);
                        return null;
                    }

                    return text;

                case FieldType.Choice or FieldType.Hidden:
                    if (!field.Options!.Contains(text, StringComparer.Ordinal))
                    {
                        Error(path, ValidationCodes.InvalidOption, label);
                        return null;
                    }

                    return text;

                case FieldType.Site:
                    if (!_context.ActiveSites.Contains(text, StringComparer.Ordinal))
                    {
                        Error(path, ValidationCodes.InvalidOption, label);
                        return null;
                    }

                    return text;

                default:
                    Error(path, ValidationCodes.InvalidType, label);
                    return null;
            }
        }

        private JsonNode? ValidateConsent(FieldDefinition field, JsonElement? raw, bool present, string path, string label)
        {
            if (present && raw!.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                Error(path, ValidationCodes.InvalidType, label);
                return null;
            }

            var accepted = present && raw!.Value.ValueKind == JsonValueKind.True;
            if (!accepted && field.Required)
            {
                Error(path, ValidationCodes.MustBeAccepted, label);
                return null;
            }

            return JsonValue.Create(accepted);
        }

        private JsonNode? ValidateMultichoice(FieldDefinition field, JsonElement? raw, bool present, string path, string label)
        {
            if (present && raw!.Value.ValueKind != JsonValueKind.Array)
            {
                Error(path, ValidationCodes.InvalidType, label);
                return null;
            }

            var values = new List<string>();
            if (present)
            {
                foreach (var item in raw!.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        Error(path, ValidationCodes.InvalidType, label);
                        return null;
                    }

                    values.Add(TextNormalizer.SingleLine(item.GetString()!));
                }
            }

            if (values.Count == 0)
            {
                if (field.Required || field.Min > 0)
                {
                    Error(path, ValidationCodes.Required, label);
                }

                return null;
            }

            if (values.Any(v => !field.Options!.Contains(v, StringComparer.Ordinal)))
            {
                Error(path, ValidationCodes.InvalidOption, label);
                return null;
            }

            if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            {
                Error(path, ValidationCodes.DuplicateOption, label);
                return null;
            }

            if (!CheckCount(field, values.Count, path, label))
            {
                return null;
            }

            // Ordre de la définition, pour un stockage stable.
            return new JsonArray(field.Options!.Where(values.Contains).Select(v => (JsonNode)JsonValue.Create(v)).ToArray());
        }

        private JsonNode? ValidateAddress(FieldDefinition field, JsonElement? raw, bool present, string path, string label)
        {
            if (present && raw!.Value.ValueKind != JsonValueKind.Object)
            {
                Error(path, ValidationCodes.InvalidType, label);
                return null;
            }

            string? Part(string name)
            {
                if (!present || !raw!.Value.TryGetProperty(name, out var part) || part.ValueKind == JsonValueKind.Null)
                {
                    return string.Empty;
                }

                return part.ValueKind == JsonValueKind.String ? TextNormalizer.SingleLine(part.GetString()!) : null;
            }

            var parts = new[] { ("street", "adresse"), ("postalCode", "code postal"), ("city", "ville") }
                .Select(p => (Name: p.Item1, Label: $"{label} : {p.Item2}", Value: Part(p.Item1)))
                .ToArray();

            if (parts.Any(p => p.Value is null))
            {
                Error(path, ValidationCodes.InvalidType, label);
                return null;
            }

            if (parts.All(p => p.Value!.Length == 0))
            {
                if (field.Required)
                {
                    Error(path, ValidationCodes.Required, label);
                }

                return null;
            }

            // Une adresse commencée doit être complète.
            var result = new JsonObject();
            var valid = true;
            foreach (var (name, partLabel, value) in parts)
            {
                var partPath = $"{path}.{name}";
                if (value!.Length == 0)
                {
                    Error(partPath, ValidationCodes.Required, partLabel);
                    valid = false;
                }
                else if (value.Length > FieldLimits.TextDefaultMaxLength)
                {
                    Error(partPath, ValidationCodes.TooLong, partLabel, FieldLimits.TextDefaultMaxLength.ToString(CultureInfo.InvariantCulture));
                    valid = false;
                }
                else if (name == "postalCode" && !PostalCodePattern().IsMatch(value))
                {
                    Error(partPath, ValidationCodes.InvalidPostalCode, partLabel);
                    valid = false;
                }
                else
                {
                    result[name] = name == "city" ? value.ToUpperInvariant() : value;
                }
            }

            return valid ? result : null;
        }

        private JsonNode? ValidateRepeat(FieldDefinition field, JsonElement? raw, bool present, string path, string label)
        {
            if (present && raw!.Value.ValueKind != JsonValueKind.Array)
            {
                Error(path, ValidationCodes.InvalidType, label);
                return null;
            }

            var rows = present ? raw!.Value.EnumerateArray().ToList() : [];
            var max = field.Max ?? FormDefinitionParser.MaxRepeatRows;
            if (rows.Count > max)
            {
                // Refus avant de parcourir les lignes : borne le travail sur une saisie hostile.
                Error(path, ValidationCodes.TooManyItems, label, max.ToString(CultureInfo.InvariantCulture));
                return null;
            }

            if (rows.Count == 0)
            {
                if (field.Required || field.Min > 0)
                {
                    Error(path, ValidationCodes.Required, label);
                }

                return null;
            }

            if (!CheckCount(field, rows.Count, path, label))
            {
                return null;
            }

            var definitions = field.Fields!.ToDictionary(f => f.Id, StringComparer.Ordinal);
            var result = new JsonArray();
            var errorsBefore = _errors.Count;

            for (var i = 0; i < rows.Count; i++)
            {
                var rowPath = $"{path}[{i}]";
                if (rows[i].ValueKind != JsonValueKind.Object)
                {
                    Error(rowPath, ValidationCodes.InvalidType, label);
                    continue;
                }

                var row = new Scope(definitions, rows[i], _top);
                row.VisibilityRule = sub => sub.VisibleIf is null || Evaluate(sub.VisibleIf, row);

                var rowOutput = new JsonObject();
                foreach (var sub in field.Fields!)
                {
                    ProcessField(sub, row, $"{rowPath}.{sub.Id}", rowOutput);
                }

                result.Add(rowOutput);
            }

            return _errors.Count == errorsBefore ? result : null;
        }

        private bool CheckCount(FieldDefinition field, int count, string path, string label)
        {
            if (field.Min is { } min && count < min)
            {
                Error(path, ValidationCodes.TooFewItems, label, min.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            if (field.Max is { } max && count > max)
            {
                Error(path, ValidationCodes.TooManyItems, label, max.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            return true;
        }

        private void ApplyRules(FieldDefinition field, Scope scope, string path)
        {
            var label = field.Label ?? field.Id;
            var node = scope.Normalized[field.Id];

            foreach (var rule in field.Rules!)
            {
                if (rule.Op == RuleOperator.SameAs)
                {
                    var other = scope.Lookup(rule.Field!);
                    if (other.Node is not null && !JsonNode.DeepEquals(node, other.Node))
                    {
                        Error(path, ValidationCodes.Rule(rule.Op), label, other.Definition!.Label ?? other.Definition.Id);
                        return;
                    }

                    continue;
                }

                var value = DateOnly.ParseExact(node.GetValue<string>(), DateExpression.DateFormat, CultureInfo.InvariantCulture);
                DateOnly target;
                string detail;

                if (rule.Op is RuleOperator.Past or RuleOperator.Future)
                {
                    target = _context.Today;
                    detail = string.Empty;
                }
                else if (rule.Field is not null)
                {
                    var other = scope.Lookup(rule.Field);
                    if (other.Node is null)
                    {
                        // Cible masquée, vide ou invalide : son propre contrôle s'en charge.
                        continue;
                    }

                    target = DateOnly.ParseExact(other.Node.GetValue<string>(), DateExpression.DateFormat, CultureInfo.InvariantCulture);
                    detail = $"« {other.Definition!.Label ?? other.Definition.Id} »";
                }
                else
                {
                    DateExpression.TryResolve(rule.Value, _context.Today, out target);
                    detail = $"au {target.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
                }

                var ok = rule.Op switch
                {
                    RuleOperator.After or RuleOperator.Future => value > target,
                    RuleOperator.Before or RuleOperator.Past => value < target,
                    RuleOperator.NotBefore => value >= target,
                    _ => value <= target,
                };

                if (!ok)
                {
                    Error(path, ValidationCodes.Rule(rule.Op), label, detail);
                    return;
                }
            }
        }

        internal static bool Evaluate(Condition condition, Scope scope)
        {
            if (condition.All is not null)
            {
                return condition.All.All(c => Evaluate(c, scope));
            }

            if (condition.Any is not null)
            {
                return condition.Any.Any(c => Evaluate(c, scope));
            }

            var actual = scope.VisibleRaw(condition.Field!);
            var values = ConditionValues(actual);

            return condition.Op switch
            {
                ConditionOperator.Empty => values.Count == 0,
                ConditionOperator.NotEmpty => values.Count > 0,
                ConditionOperator.Equals => values.Contains(Comparable(condition.Value!.Value)),
                ConditionOperator.NotEquals => !values.Contains(Comparable(condition.Value!.Value)),
                ConditionOperator.In => condition.Value!.Value.EnumerateArray().Any(v => values.Contains(Comparable(v))),
                _ => !condition.Value!.Value.EnumerateArray().Any(v => values.Contains(Comparable(v))),
            };
        }

        /// <summary>Valeurs comparables d'une saisie brute ; une chaîne vide ou un faux consentement compte comme vide.</summary>
        private static HashSet<string> ConditionValues(JsonElement? raw)
        {
            var values = new HashSet<string>(StringComparer.Ordinal);
            if (raw is not { } element)
            {
                return values;
            }

            var items = element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToList() : [element];
            foreach (var item in items)
            {
                if (item.ValueKind is JsonValueKind.Null or JsonValueKind.False or JsonValueKind.Undefined)
                {
                    continue;
                }

                var value = Comparable(item);
                if (value.Length > 0)
                {
                    values.Add(value);
                }
            }

            return values;
        }

        private static string Comparable(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => TextNormalizer.SingleLine(element.GetString()!),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.GetRawText(),
        };

        private void Error(string path, string code, string label, string? detail = null) =>
            _errors.Add(new ValidationError(path, code, ValidationMessages.For(code, label, detail)));
    }

    /// <summary>
    /// Parcours des sections : une section est affichée si elle est sur le parcours et que sa condition est vraie.
    /// À la fin d'une section affichée, le premier saut applicable mène plus loin ou termine le formulaire ;
    /// les sections sautées sont hors du parcours. Calcul progressif : un saut ne dépend que de champs
    /// des sections déjà parcourues (contrôlé à la publication, avec les cycles).
    /// </summary>
    private sealed class FormPath(FormDefinition definition, Scope scope)
    {
        private readonly Dictionary<SectionDefinition, bool> _shown = [];
        private int _next;
        private int _target;
        private string? _ending;

        public string? Ending
        {
            get
            {
                Advance(definition.Sections.Count);
                return _ending;
            }
        }

        public bool IsSectionShown(SectionDefinition section)
        {
            Advance(IndexOf(section) + 1);
            return _shown[section];
        }

        private int IndexOf(SectionDefinition section)
        {
            for (var i = 0; i < definition.Sections.Count; i++)
            {
                if (ReferenceEquals(definition.Sections[i], section))
                {
                    return i;
                }
            }

            throw new InvalidOperationException("Section inconnue.");
        }

        private void Advance(int count)
        {
            while (_next < count)
            {
                var index = _next++;
                var section = definition.Sections[index];
                var onPath = _ending is null && index >= _target;

                // Marquée avant l'évaluation : ses propres champs peuvent être consultés par sa condition ou ses sauts.
                _shown[section] = onPath;
                if (!onPath)
                {
                    continue;
                }

                var shown = section.VisibleIf is null || Run.Evaluate(section.VisibleIf, scope);
                _shown[section] = shown;
                if (!shown)
                {
                    continue;
                }

                var jump = section.Jumps?.FirstOrDefault(j => j.If is null || Run.Evaluate(j.If, scope));
                if (jump?.End is not null)
                {
                    _ending = jump.End;
                }
                else if (jump?.GoTo is not null)
                {
                    _target = definition.Sections.ToList().FindIndex(s => s.Id == jump.GoTo);
                }
            }
        }
    }

    private sealed class Scope(Dictionary<string, FieldDefinition> definitions, JsonElement raw, Scope? parent)
    {
        private readonly Dictionary<string, bool> _visibility = new(StringComparer.Ordinal);

        public Func<FieldDefinition, bool> VisibilityRule { get; set; } = _ => true;

        public Dictionary<string, JsonNode> Normalized { get; } = new(StringComparer.Ordinal);

        public bool IsVisible(string id)
        {
            if (!_visibility.TryGetValue(id, out var visible))
            {
                visible = VisibilityRule(definitions[id]);
                _visibility[id] = visible;
            }

            return visible;
        }

        public JsonElement? Raw(string id) => raw.TryGetProperty(id, out var value) ? value : null;

        /// <summary>Valeur brute d'un champ du groupe courant, sinon du formulaire ; null si le champ est masqué.</summary>
        public JsonElement? VisibleRaw(string id)
        {
            if (definitions.ContainsKey(id))
            {
                return IsVisible(id) ? Raw(id) : null;
            }

            return parent?.VisibleRaw(id);
        }

        public (FieldDefinition? Definition, JsonNode? Node) Lookup(string id)
        {
            if (definitions.TryGetValue(id, out var definition))
            {
                return (definition, Normalized.GetValueOrDefault(id));
            }

            return parent?.Lookup(id) ?? (null, null);
        }
    }

    private static bool Matches(string pattern, string value)
    {
        var regex = Patterns.GetOrAdd(pattern, p => new Regex(p, RegexOptions.CultureInvariant, FormDefinitionParser.RegexTimeout));
        try
        {
            return regex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"[\s.\-()]")]
    private static partial Regex PhoneSeparators();

    [GeneratedRegex(@"^\+?[0-9]{6,15}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex("^[0-9]{5}$")]
    private static partial Regex PostalCodePattern();
}
