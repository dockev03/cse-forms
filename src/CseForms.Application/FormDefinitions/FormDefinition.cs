using System.Text.Json;
using System.Text.Json.Serialization;

namespace CseForms.Application.FormDefinitions;

/// <summary>
/// Définition d'un formulaire, lue depuis le JSON d'une <c>form_version</c>.
/// Le même JSON pilote le rendu client et la validation serveur, qui fait foi.
/// Les propriétés de premier niveau autres que <c>sections</c> (notifications, accusé...)
/// sont traitées par d'autres composants et ignorées ici.
/// </summary>
public sealed record FormDefinition
{
    public required IReadOnlyList<SectionDefinition> Sections { get; init; }

    public IEnumerable<FieldDefinition> AllTopLevelFields => Sections.SelectMany(s => s.Fields);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SectionDefinition
{
    public required string Id { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    /// <summary>Affichage replié par défaut (présentation uniquement).</summary>
    public bool Collapsed { get; init; }

    public Condition? VisibleIf { get; init; }

    public required IReadOnlyList<FieldDefinition> Fields { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FieldDefinition
{
    public required string Id { get; init; }

    public required FieldType Type { get; init; }

    public string? Label { get; init; }

    /// <summary>Contenu d'un champ <c>info</c>, ou aide affichée sous le champ.</summary>
    public string? Text { get; init; }

    public string? Help { get; init; }

    public string? Placeholder { get; init; }

    public bool Required { get; init; }

    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }

    public string? Pattern { get; init; }

    public TextTransform Transform { get; init; }

    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>Présentation d'un choix unique : <c>radio</c> ou <c>select</c>.</summary>
    public string? Display { get; init; }

    /// <summary>Nombre minimal d'éléments (lignes d'un groupe, cases cochées).</summary>
    public int? Min { get; init; }

    /// <summary>Nombre maximal d'éléments (lignes d'un groupe, cases cochées).</summary>
    public int? Max { get; init; }

    /// <summary>Libellé du bouton d'ajout d'une ligne de groupe répétable.</summary>
    public string? AddLabel { get; init; }

    public IReadOnlyList<FieldDefinition>? Fields { get; init; }

    public Condition? VisibleIf { get; init; }

    public IReadOnlyList<FieldRule>? Rules { get; init; }

    /// <summary>Source de préremplissage côté client, par exemple <c>url</c>. La valeur reste validée par liste blanche.</summary>
    public string? Prefill { get; init; }

    /// <summary>Faux pour un champ de contrôle (confirmation d'email) : validé mais jamais enregistré.</summary>
    public bool Persist { get; init; } = true;
}

[JsonConverter(typeof(JsonStringEnumConverter<FieldType>))]
public enum FieldType
{
    Text,
    Textarea,
    Email,
    Phone,
    Date,
    Choice,
    Multichoice,
    Site,
    Address,
    Repeat,
    Consent,
    Info,
    Hidden,
}

[JsonConverter(typeof(JsonStringEnumConverter<TextTransform>))]
public enum TextTransform
{
    None,
    Upper,
    Lower,
    Capitalize,
}

/// <summary>
/// Condition d'affichage : soit une comparaison (<c>field</c>, <c>op</c>, <c>value</c>),
/// soit une combinaison <c>all</c> ou <c>any</c>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Condition
{
    public string? Field { get; init; }

    public ConditionOperator? Op { get; init; }

    public JsonElement? Value { get; init; }

    public IReadOnlyList<Condition>? All { get; init; }

    public IReadOnlyList<Condition>? Any { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ConditionOperator>))]
public enum ConditionOperator
{
    Equals,
    NotEquals,
    In,
    NotIn,
    Empty,
    NotEmpty,
}

/// <summary>
/// Règle de validation croisée. La cible est un autre champ (<c>field</c>) ou une valeur
/// (<c>value</c>) ; pour les dates, <c>today</c>, <c>today+6M</c>, <c>today-18Y</c>, <c>today+30D</c> ou <c>AAAA-MM-JJ</c>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FieldRule
{
    public required RuleOperator Op { get; init; }

    public string? Field { get; init; }

    public string? Value { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<RuleOperator>))]
public enum RuleOperator
{
    After,
    Before,
    NotBefore,
    NotAfter,
    Past,
    Future,
    SameAs,
}
