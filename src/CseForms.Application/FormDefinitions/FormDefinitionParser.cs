using System.Text.Json;
using System.Text.RegularExpressions;

namespace CseForms.Application.FormDefinitions;

/// <summary>
/// Lit et contrôle une définition de formulaire. Une définition refusée ici ne peut pas être publiée.
/// </summary>
public static partial class FormDefinitionParser
{
    public const int MaxRepeatRows = 100;
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32,
    };

    private static readonly HashSet<FieldType> TextTypes = [FieldType.Text, FieldType.Textarea];
    private static readonly HashSet<FieldType> OptionTypes = [FieldType.Choice, FieldType.Multichoice, FieldType.Hidden];
    private static readonly HashSet<RuleOperator> DateRules =
        [RuleOperator.After, RuleOperator.Before, RuleOperator.NotBefore, RuleOperator.NotAfter, RuleOperator.Past, RuleOperator.Future];

    public static DefinitionParseResult Parse(string json)
    {
        FormDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<FormDefinition>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return new DefinitionParseResult(null, [new DefinitionError(ex.Path ?? "$", "JSON invalide ou propriété inconnue.")]);
        }

        if (definition?.Sections is null)
        {
            return new DefinitionParseResult(null, [new DefinitionError("$", "La définition doit contenir des sections.")]);
        }

        var errors = new List<DefinitionError>();
        new Checker(definition, errors).Run();

        return new DefinitionParseResult(errors.Count == 0 ? definition : null, errors);
    }

    private sealed class Checker(FormDefinition definition, List<DefinitionError> errors)
    {
        private readonly Dictionary<string, FieldDefinition> _topLevel = new(StringComparer.Ordinal);

        public void Run()
        {
            if (definition.Sections.Count == 0)
            {
                Error("$.sections", "Au moins une section est requise.");
                return;
            }

            CollectTopLevelFields();

            var sectionIds = new HashSet<string>(StringComparer.Ordinal);
            for (var s = 0; s < definition.Sections.Count; s++)
            {
                var section = definition.Sections[s];
                var path = $"$.sections[{s}]";
                if (section is null)
                {
                    Error(path, "Section vide.");
                    continue;
                }

                CheckId(section.Id, $"{path}.id");
                if (!sectionIds.Add(section.Id ?? string.Empty))
                {
                    Error($"{path}.id", "Identifiant de section en double.");
                }

                if (section.VisibleIf is not null)
                {
                    CheckCondition(section.VisibleIf, $"{path}.visibleIf", rowScope: null);
                }

                if (section.Fields is null || section.Fields.Count == 0)
                {
                    Error($"{path}.fields", "Une section doit contenir au moins un champ.");
                    continue;
                }

                for (var f = 0; f < section.Fields.Count; f++)
                {
                    CheckField(section.Fields[f], $"{path}.fields[{f}]", rowScope: null);
                }
            }

            CheckConditionCycles();
        }

        private void CollectTopLevelFields()
        {
            for (var s = 0; s < definition.Sections.Count; s++)
            {
                var fields = definition.Sections[s]?.Fields ?? [];
                for (var f = 0; f < fields.Count; f++)
                {
                    var field = fields[f];
                    if (field?.Id is null)
                    {
                        continue;
                    }

                    if (!_topLevel.TryAdd(field.Id, field))
                    {
                        Error($"$.sections[{s}].fields[{f}].id", "Identifiant de champ en double dans le formulaire.");
                    }
                }
            }
        }

        private void CheckField(FieldDefinition? field, string path, IReadOnlyDictionary<string, FieldDefinition>? rowScope)
        {
            if (field is null)
            {
                Error(path, "Champ vide.");
                return;
            }

            CheckId(field.Id, $"{path}.id");

            if (field.Type == FieldType.Info)
            {
                if (string.IsNullOrWhiteSpace(field.Text) && string.IsNullOrWhiteSpace(field.Label))
                {
                    Error(path, "Un texte d'information doit avoir un libellé ou un texte.");
                }

                if (field.Required)
                {
                    Error($"{path}.required", "Un texte d'information ne peut pas être obligatoire.");
                }
            }
            else if (string.IsNullOrWhiteSpace(field.Label) && field.Type != FieldType.Hidden)
            {
                Error($"{path}.label", "Le libellé est obligatoire.");
            }

            CheckLengths(field, path);
            CheckPattern(field, path);
            CheckOptions(field, path);
            CheckCounts(field, path);
            CheckChildren(field, path, rowScope);

            if (field.Transform != TextTransform.None && !TextTypes.Contains(field.Type))
            {
                Error($"{path}.transform", "La transformation ne s'applique qu'aux champs texte.");
            }

            if (field.Display is not null && field.Display is not ("radio" or "select"))
            {
                Error($"{path}.display", "Présentation attendue : radio ou select.");
            }

            if (field.Prefill is not null && field.Prefill != "url")
            {
                Error($"{path}.prefill", "Seul le préremplissage par URL est pris en charge.");
            }

            if (field.VisibleIf is not null)
            {
                CheckCondition(field.VisibleIf, $"{path}.visibleIf", rowScope);
            }

            CheckRules(field, path, rowScope);
        }

        private void CheckLengths(FieldDefinition field, string path)
        {
            if (field.MinLength is null && field.MaxLength is null)
            {
                return;
            }

            if (!TextTypes.Contains(field.Type))
            {
                Error(path, "Les longueurs ne s'appliquent qu'aux champs texte.");
                return;
            }

            if (field.MinLength is < 0 || field.MaxLength is <= 0 || field.MinLength > field.MaxLength)
            {
                Error(path, "Longueurs minimale et maximale incohérentes.");
            }

            if (field.MaxLength > FieldLimits.MaxLengthCeiling(field.Type))
            {
                Error($"{path}.maxLength", $"Longueur maximale limitée à {FieldLimits.MaxLengthCeiling(field.Type)}.");
            }
        }

        private void CheckPattern(FieldDefinition field, string path)
        {
            if (field.Pattern is null)
            {
                return;
            }

            if (!TextTypes.Contains(field.Type))
            {
                Error($"{path}.pattern", "Le format ne s'applique qu'aux champs texte.");
                return;
            }

            try
            {
                _ = new Regex(field.Pattern, RegexOptions.CultureInvariant, RegexTimeout);
            }
            catch (ArgumentException)
            {
                Error($"{path}.pattern", "Expression régulière invalide.");
            }
        }

        private void CheckOptions(FieldDefinition field, string path)
        {
            if (!OptionTypes.Contains(field.Type))
            {
                if (field.Options is not null)
                {
                    Error($"{path}.options", "Les options ne s'appliquent qu'aux choix et aux champs cachés.");
                }

                return;
            }

            if (field.Options is null || field.Options.Count == 0)
            {
                Error($"{path}.options", "Au moins une option est requise.");
                return;
            }

            if (field.Options.Any(string.IsNullOrWhiteSpace))
            {
                Error($"{path}.options", "Une option ne peut pas être vide.");
            }

            if (field.Options.Distinct(StringComparer.Ordinal).Count() != field.Options.Count)
            {
                Error($"{path}.options", "Options en double.");
            }
        }

        private void CheckCounts(FieldDefinition field, string path)
        {
            if (field.Min is null && field.Max is null)
            {
                return;
            }

            if (field.Type is not (FieldType.Repeat or FieldType.Multichoice))
            {
                Error(path, "Les bornes min et max ne s'appliquent qu'aux groupes répétables et aux choix multiples.");
                return;
            }

            if (field.Min is < 0 || field.Max is <= 0 || field.Min > field.Max)
            {
                Error(path, "Bornes min et max incohérentes.");
            }

            if (field.Type == FieldType.Repeat && field.Max > MaxRepeatRows)
            {
                Error($"{path}.max", $"Un groupe répétable est limité à {MaxRepeatRows} lignes.");
            }
        }

        private void CheckChildren(FieldDefinition field, string path, IReadOnlyDictionary<string, FieldDefinition>? rowScope)
        {
            if (field.Type != FieldType.Repeat)
            {
                if (field.Fields is not null)
                {
                    Error($"{path}.fields", "Seul un groupe répétable contient des champs.");
                }

                return;
            }

            if (rowScope is not null)
            {
                Error(path, "Un groupe répétable ne peut pas en contenir un autre.");
                return;
            }

            if (field.Fields is null || field.Fields.Count == 0)
            {
                Error($"{path}.fields", "Un groupe répétable doit contenir au moins un champ.");
                return;
            }

            var row = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal);
            for (var i = 0; i < field.Fields.Count; i++)
            {
                if (field.Fields[i]?.Id is { } id && !row.TryAdd(id, field.Fields[i]))
                {
                    Error($"{path}.fields[{i}].id", "Identifiant de champ en double dans le groupe.");
                }
            }

            for (var i = 0; i < field.Fields.Count; i++)
            {
                CheckField(field.Fields[i], $"{path}.fields[{i}]", row);
            }
        }

        private void CheckRules(FieldDefinition field, string path, IReadOnlyDictionary<string, FieldDefinition>? rowScope)
        {
            if (field.Rules is null)
            {
                return;
            }

            for (var i = 0; i < field.Rules.Count; i++)
            {
                var rule = field.Rules[i];
                var rulePath = $"{path}.rules[{i}]";
                if (rule is null)
                {
                    Error(rulePath, "Règle vide.");
                    continue;
                }

                if (DateRules.Contains(rule.Op) && field.Type != FieldType.Date)
                {
                    Error(rulePath, "Cette règle ne s'applique qu'aux dates.");
                    continue;
                }

                var needsTarget = rule.Op is not (RuleOperator.Past or RuleOperator.Future);
                if (!needsTarget)
                {
                    if (rule.Field is not null || rule.Value is not null)
                    {
                        Error(rulePath, "Les règles past et future n'ont pas de cible.");
                    }

                    continue;
                }

                if ((rule.Field is null) == (rule.Value is null))
                {
                    Error(rulePath, "Indiquer soit un champ, soit une valeur.");
                    continue;
                }

                if (rule.Value is not null)
                {
                    if (rule.Op == RuleOperator.SameAs)
                    {
                        Error(rulePath, "La règle sameAs compare à un autre champ.");
                    }
                    else if (!DateExpression.IsValid(rule.Value))
                    {
                        Error($"{rulePath}.value", "Date attendue : AAAA-MM-JJ, today, today+6M, today-18Y, today+30D.");
                    }

                    continue;
                }

                var target = Resolve(rule.Field!, rowScope);
                if (target is null)
                {
                    Error($"{rulePath}.field", "Champ cible inconnu.");
                }
                else if (target.Id == field.Id)
                {
                    Error($"{rulePath}.field", "Un champ ne peut pas se comparer à lui-même.");
                }
                else if (rule.Op == RuleOperator.SameAs ? target.Type != field.Type : target.Type != FieldType.Date)
                {
                    Error($"{rulePath}.field", "Le champ cible n'est pas du bon type.");
                }
            }
        }

        private void CheckCondition(Condition condition, string path, IReadOnlyDictionary<string, FieldDefinition>? rowScope)
        {
            var isLeaf = condition.Field is not null || condition.Op is not null;
            var combinations = (condition.All is not null ? 1 : 0) + (condition.Any is not null ? 1 : 0);

            if (isLeaf == (combinations > 0) || combinations > 1)
            {
                Error(path, "Une condition est soit une comparaison, soit une combinaison all ou any.");
                return;
            }

            if (!isLeaf)
            {
                var children = condition.All ?? condition.Any!;
                if (children.Count == 0)
                {
                    Error(path, "Une combinaison doit contenir au moins une condition.");
                }

                var key = condition.All is not null ? "all" : "any";
                for (var i = 0; i < children.Count; i++)
                {
                    CheckCondition(children[i], $"{path}.{key}[{i}]", rowScope);
                }

                return;
            }

            if (condition.Field is null || condition.Op is null)
            {
                Error(path, "Une comparaison demande un champ et un opérateur.");
                return;
            }

            var target = Resolve(condition.Field, rowScope);
            if (target is null || target.Type is FieldType.Info or FieldType.Repeat)
            {
                Error($"{path}.field", "Champ de condition inconnu ou non comparable.");
            }

            var hasValue = condition.Value is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null };
            switch (condition.Op)
            {
                case ConditionOperator.Empty or ConditionOperator.NotEmpty when hasValue:
                    Error($"{path}.value", "Les opérateurs empty et notEmpty n'ont pas de valeur.");
                    break;
                case ConditionOperator.Equals or ConditionOperator.NotEquals
                    when !hasValue || condition.Value!.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object:
                    Error($"{path}.value", "Une valeur simple est attendue.");
                    break;
                case ConditionOperator.In or ConditionOperator.NotIn
                    when !hasValue || condition.Value!.Value.ValueKind != JsonValueKind.Array:
                    Error($"{path}.value", "Une liste de valeurs est attendue.");
                    break;
            }
        }

        private void CheckConditionCycles()
        {
            // Graphe des dépendances d'affichage entre champs de premier niveau
            // (la condition d'une section s'applique à chacun de ses champs).
            var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var section in definition.Sections.OfType<SectionDefinition>())
            {
                var sectionRefs = References(section.VisibleIf);
                foreach (var field in section.Fields ?? [])
                {
                    if (field?.Id is null)
                    {
                        continue;
                    }

                    var refs = new HashSet<string>(sectionRefs, StringComparer.Ordinal);
                    refs.UnionWith(References(field.VisibleIf));
                    dependencies[field.Id] = refs;
                }
            }

            var state = new Dictionary<string, bool>(StringComparer.Ordinal); // false : en cours, true : terminé
            foreach (var id in dependencies.Keys)
            {
                if (HasCycle(id, dependencies, state))
                {
                    Error("$.sections", $"Conditions d'affichage circulaires autour du champ '{id}'.");
                    return;
                }
            }
        }

        private static bool HasCycle(string id, Dictionary<string, HashSet<string>> dependencies, Dictionary<string, bool> state)
        {
            if (state.TryGetValue(id, out var done))
            {
                return !done;
            }

            state[id] = false;
            foreach (var next in dependencies.GetValueOrDefault(id) ?? [])
            {
                if (HasCycle(next, dependencies, state))
                {
                    return true;
                }
            }

            state[id] = true;
            return false;
        }

        private static IEnumerable<string> References(Condition? condition)
        {
            if (condition is null)
            {
                yield break;
            }

            if (condition.Field is not null)
            {
                yield return condition.Field;
            }

            foreach (var child in (condition.All ?? []).Concat(condition.Any ?? []))
            {
                foreach (var reference in References(child))
                {
                    yield return reference;
                }
            }
        }

        private FieldDefinition? Resolve(string id, IReadOnlyDictionary<string, FieldDefinition>? rowScope) =>
            rowScope?.GetValueOrDefault(id) ?? _topLevel.GetValueOrDefault(id);

        private void CheckId(string? id, string path)
        {
            if (id is null || id.Length > 64 || !IdPattern().IsMatch(id))
            {
                Error(path, "Identifiant attendu : lettres, chiffres et _, commençant par une lettre (64 caractères maximum).");
            }
        }

        private void Error(string path, string message) => errors.Add(new DefinitionError(path, message));
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex IdPattern();
}
