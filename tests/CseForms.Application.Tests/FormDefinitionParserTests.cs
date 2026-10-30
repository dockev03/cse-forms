using CseForms.Application.FormDefinitions;

namespace CseForms.Application.Tests;

public class FormDefinitionParserTests
{
    private static DefinitionParseResult Parse(string fieldsJson, string? sectionExtra = null) =>
        FormDefinitionParser.Parse($$"""{"sections":[{"id":"s1"{{sectionExtra}},"fields":[{{fieldsJson}}]}]}""");

    private static void AssertInvalid(DefinitionParseResult result, string expectedPathFragment)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Definition);
        Assert.Contains(result.Errors, e => e.Path.Contains(expectedPathFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void Valid_definition_is_parsed()
    {
        var result = Parse("""{"id":"nom","type":"text","label":"Nom","required":true,"transform":"upper"}""");

        Assert.True(result.IsValid);
        var field = Assert.Single(result.Definition!.AllTopLevelFields);
        Assert.Equal(FieldType.Text, field.Type);
        Assert.Equal(TextTransform.Upper, field.Transform);
        Assert.True(field.Persist);
    }

    [Fact]
    public void Other_top_level_properties_are_ignored()
    {
        var result = FormDefinitionParser.Parse(
            """{"slug":"inscription","notifications":[],"sections":[{"id":"s","fields":[{"id":"a","type":"text","label":"A"}]}]}""");

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"sections":[]}""")]
    [InlineData("""{"sections":[{"id":"s","fields":[]}]}""")]
    [InlineData("""{"sections":[null]}""")]
    [InlineData("""{"sections":[{"id":"s","fields":[null]}]}""")]
    public void Malformed_definition_is_rejected(string json) =>
        Assert.False(FormDefinitionParser.Parse(json).IsValid);

    [Fact]
    public void Unknown_field_property_is_rejected() =>
        AssertInvalid(Parse("""{"id":"nom","type":"text","label":"Nom","requird":true}"""), "$");

    [Fact]
    public void Unknown_type_is_rejected() =>
        Assert.False(Parse("""{"id":"nom","type":"rating","label":"Nom"}""").IsValid);

    [Theory]
    [InlineData("1nom")]
    [InlineData("nom-complet")]
    [InlineData("")]
    public void Invalid_identifier_is_rejected(string id) =>
        AssertInvalid(Parse($$"""{"id":"{{id}}","type":"text","label":"Nom"}"""), ".id");

    [Fact]
    public void Duplicate_identifiers_are_rejected_across_sections()
    {
        var result = FormDefinitionParser.Parse(
            """{"sections":[{"id":"a","fields":[{"id":"x","type":"text","label":"X"}]},{"id":"b","fields":[{"id":"x","type":"text","label":"X"}]}]}""");

        AssertInvalid(result, "sections[1].fields[0].id");
    }

    [Fact]
    public void Label_is_required_except_for_info_and_hidden()
    {
        AssertInvalid(Parse("""{"id":"nom","type":"text"}"""), ".label");
        Assert.True(Parse("""{"id":"info","type":"info","text":"Texte"},{"id":"src","type":"hidden","options":["a"]}""").IsValid);
    }

    [Theory]
    [InlineData("""{"id":"c","type":"choice","label":"C"}""")]
    [InlineData("""{"id":"c","type":"choice","label":"C","options":[]}""")]
    [InlineData("""{"id":"c","type":"choice","label":"C","options":["a","a"]}""")]
    [InlineData("""{"id":"c","type":"choice","label":"C","options":[" "]}""")]
    [InlineData("""{"id":"t","type":"text","label":"T","options":["a"]}""")]
    public void Options_are_checked(string field) => AssertInvalid(Parse(field), ".options");

    [Theory]
    [InlineData("""{"id":"t","type":"text","label":"T","pattern":"[a-"}""")]
    [InlineData("""{"id":"t","type":"date","label":"T","pattern":"^a$"}""")]
    public void Pattern_is_checked(string field) => AssertInvalid(Parse(field), ".pattern");

    [Theory]
    [InlineData("""{"id":"t","type":"text","label":"T","minLength":5,"maxLength":2}""")]
    [InlineData("""{"id":"t","type":"text","label":"T","maxLength":5000}""")]
    [InlineData("""{"id":"t","type":"date","label":"T","maxLength":10}""")]
    [InlineData("""{"id":"r","type":"repeat","label":"R","max":500,"fields":[{"id":"a","type":"text","label":"A"}]}""")]
    [InlineData("""{"id":"t","type":"text","label":"T","max":3}""")]
    public void Bounds_are_checked(string field) => AssertInvalid(Parse(field), "fields[0]");

    [Theory]
    [InlineData("""{"id":"r","type":"repeat","label":"R"}""")]
    [InlineData("""{"id":"r","type":"repeat","label":"R","fields":[{"id":"n","type":"repeat","label":"N","fields":[{"id":"a","type":"text","label":"A"}]}]}""")]
    [InlineData("""{"id":"r","type":"repeat","label":"R","fields":[{"id":"a","type":"text","label":"A"},{"id":"a","type":"text","label":"A"}]}""")]
    [InlineData("""{"id":"t","type":"text","label":"T","fields":[{"id":"a","type":"text","label":"A"}]}""")]
    public void Repeat_children_are_checked(string field) => AssertInvalid(Parse(field), "fields[0]");

    [Fact]
    public void Repeat_children_may_reuse_top_level_identifiers() =>
        Assert.True(Parse("""{"id":"nom","type":"text","label":"Nom"},{"id":"enfants","type":"repeat","label":"Enfants","fields":[{"id":"nom","type":"text","label":"Nom"}]}""").IsValid);

    [Theory]
    [InlineData("""{"field":"inconnu","op":"equals","value":"a"}""")]
    [InlineData("""{"field":"c","op":"equals"}""")]
    [InlineData("""{"field":"c","op":"in","value":"a"}""")]
    [InlineData("""{"field":"c","op":"empty","value":"a"}""")]
    [InlineData("""{"field":"c","op":"equals","value":"a","all":[]}""")]
    [InlineData("""{"all":[]}""")]
    [InlineData("""{"all":[{"field":"c","op":"equals","value":"a"}],"any":[{"field":"c","op":"empty"}]}""")]
    [InlineData("""{"field":"info","op":"notEmpty"}""")]
    public void Conditions_are_checked(string condition) =>
        AssertInvalid(
            Parse($$"""{"id":"c","type":"choice","label":"C","options":["a"]},{"id":"info","type":"info","text":"I"},{"id":"t","type":"text","label":"T","visibleIf":{{condition}}}"""),
            ".visibleIf");

    [Fact]
    public void Circular_conditions_are_rejected()
    {
        var result = Parse(
            """{"id":"a","type":"text","label":"A","visibleIf":{"field":"b","op":"notEmpty"}},{"id":"b","type":"text","label":"B","visibleIf":{"field":"a","op":"notEmpty"}}""");

        AssertInvalid(result, "$.sections");
    }

    [Fact]
    public void Circular_condition_through_section_is_rejected()
    {
        var result = FormDefinitionParser.Parse(
            """{"sections":[{"id":"s","visibleIf":{"field":"a","op":"notEmpty"},"fields":[{"id":"a","type":"text","label":"A"}]}]}""");

        AssertInvalid(result, "$.sections");
    }

    [Theory]
    [InlineData("""{"id":"t","type":"text","label":"T","rules":[{"op":"past"}]}""")]
    [InlineData("""{"id":"d","type":"date","label":"D","rules":[{"op":"after"}]}""")]
    [InlineData("""{"id":"d","type":"date","label":"D","rules":[{"op":"after","field":"x","value":"today"}]}""")]
    [InlineData("""{"id":"d","type":"date","label":"D","rules":[{"op":"after","field":"inconnu"}]}""")]
    [InlineData("""{"id":"d","type":"date","label":"D","rules":[{"op":"after","field":"d"}]}""")]
    [InlineData("""{"id":"d","type":"date","label":"D","rules":[{"op":"after","field":"t"}]},{"id":"t","type":"text","label":"T"}""")]
    [InlineData("""{"id":"d","type":"date","label":"D","rules":[{"op":"notAfter","value":"demain"}]}""")]
    [InlineData("""{"id":"d","type":"date","label":"D","rules":[{"op":"notAfter","value":"today+9999Y"}]}""")]
    [InlineData("""{"id":"d","type":"date","label":"D","rules":[{"op":"past","value":"today"}]}""")]
    [InlineData("""{"id":"e","type":"email","label":"E","rules":[{"op":"sameAs","value":"x"}]}""")]
    [InlineData("""{"id":"e","type":"email","label":"E","rules":[{"op":"sameAs","field":"t"}]},{"id":"t","type":"text","label":"T"}""")]
    public void Rules_are_checked(string fields) => AssertInvalid(Parse(fields), ".rules[0]");

    [Theory]
    [InlineData("today")]
    [InlineData("today+6M")]
    [InlineData("today-18Y")]
    [InlineData("today+30D")]
    [InlineData("2027-12-31")]
    public void Date_expressions_are_accepted(string value) =>
        Assert.True(Parse($$"""{"id":"d","type":"date","label":"D","rules":[{"op":"notAfter","value":"{{value}}"}]}""").IsValid);

    [Fact]
    public void Repeat_row_rule_can_target_top_level_field() =>
        Assert.True(Parse(
            """{"id":"debut","type":"date","label":"Début"},{"id":"r","type":"repeat","label":"R","fields":[{"id":"d","type":"date","label":"D","rules":[{"op":"after","field":"debut"}]}]}""").IsValid);
}
