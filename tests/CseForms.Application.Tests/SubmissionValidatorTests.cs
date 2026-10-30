using System.Text.Json;
using CseForms.Application.FormDefinitions;

namespace CseForms.Application.Tests;

/// <summary>Cas propres au serveur ; les cas communs au client sont dans les jeux partagés.</summary>
public class SubmissionValidatorTests
{
    private static readonly ValidationContext Context = new(new DateOnly(2026, 10, 30), ["Riom"]);

    private static FormDefinition Definition(string fieldsJson)
    {
        var result = FormDefinitionParser.Parse($$"""{"sections":[{"id":"s","fields":[{{fieldsJson}}]}]}""");
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        return result.Definition!;
    }

    private static SubmissionValidationResult Validate(FormDefinition definition, string inputJson)
    {
        using var input = JsonDocument.Parse(inputJson);
        return SubmissionValidator.Validate(definition, input.RootElement, Context);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"texte\"")]
    [InlineData("null")]
    public void Non_object_input_is_rejected(string input)
    {
        var result = Validate(Definition("""{"id":"a","type":"text","label":"A"}"""), input);

        Assert.False(result.IsValid);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Error_message_never_contains_submitted_value()
    {
        var result = Validate(Definition("""{"id":"email","type":"email","label":"Email"}"""), """{"email":"secret-valeur"}""");

        var error = Assert.Single(result.Errors);
        Assert.DoesNotContain("secret-valeur", error.Message, StringComparison.Ordinal);
        Assert.Contains("Email", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_is_null_when_invalid() =>
        Assert.Null(Validate(Definition("""{"id":"a","type":"text","label":"A","required":true}"""), "{}").Data);

    [Fact]
    public void Control_characters_are_removed()
    {
        var result = Validate(Definition("""{"id":"a","type":"text","label":"A"}"""), "{\"a\":\"ab\\u0000c\\u200Bd\\te\"}");

        Assert.Equal("abcd e", result.Data!["a"]!.GetValue<string>());
    }

    [Fact]
    public void Default_max_length_applies_without_explicit_limit()
    {
        var input = JsonSerializer.Serialize(new { a = new string('x', FieldLimits.TextDefaultMaxLength + 1) });

        var result = Validate(Definition("""{"id":"a","type":"text","label":"A"}"""), input);

        Assert.Equal(ValidationCodes.TooLong, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Too_many_rows_are_rejected_without_validating_each_row()
    {
        var rows = string.Join(",", Enumerable.Repeat("{}", 11));
        var definition = Definition("""{"id":"r","type":"repeat","label":"R","max":10,"fields":[{"id":"a","type":"text","label":"A","required":true}]}""");

        var result = Validate(definition, $$"""{"r":[{{rows}}]}""");

        Assert.Equal("r tooManyItems", Assert.Single(result.Errors.Select(e => $"{e.Path} {e.Code}")));
    }

    [Fact]
    public void Row_condition_uses_row_value_then_top_level_value()
    {
        var definition = Definition(
            """
            {"id":"mode","type":"choice","label":"Mode","options":["detail","simple"]},
            {"id":"r","type":"repeat","label":"R","fields":[
              {"id":"type","type":"choice","label":"Type","options":["adulte","enfant"]},
              {"id":"age","type":"text","label":"Âge","required":true,"visibleIf":{"field":"type","op":"equals","value":"enfant"}},
              {"id":"note","type":"text","label":"Note","required":true,"visibleIf":{"field":"mode","op":"equals","value":"detail"}}
            ]}
            """);

        var result = Validate(definition, """{"mode":"simple","r":[{"type":"adulte"},{"type":"enfant"}]}""");

        Assert.Equal("r[1].age required", Assert.Single(result.Errors.Select(e => $"{e.Path} {e.Code}")));
    }

    [Fact]
    public void Condition_on_hidden_field_treats_it_as_empty()
    {
        var definition = Definition(
            """
            {"id":"a","type":"choice","label":"A","options":["oui","non"]},
            {"id":"b","type":"choice","label":"B","options":["oui","non"],"visibleIf":{"field":"a","op":"equals","value":"oui"}},
            {"id":"c","type":"text","label":"C","required":true,"visibleIf":{"field":"b","op":"equals","value":"oui"}}
            """);

        // b vaut « oui » mais est masqué : c reste masqué et n'est pas exigé.
        var result = Validate(definition, """{"a":"non","b":"oui"}""");

        Assert.True(result.IsValid);
        Assert.False(result.Data!.ContainsKey("b"));
    }

    [Fact]
    public void Rule_targeting_invalid_field_is_skipped()
    {
        var definition = Definition(
            """
            {"id":"debut","type":"date","label":"Début"},
            {"id":"fin","type":"date","label":"Fin","rules":[{"op":"after","field":"debut"}]}
            """);

        var result = Validate(definition, """{"debut":"pas une date","fin":"2026-01-01"}""");

        Assert.Equal("debut invalidDate", Assert.Single(result.Errors.Select(e => $"{e.Path} {e.Code}")));
    }

    [Fact]
    public void Relative_date_uses_context_today()
    {
        var definition = Definition("""{"id":"d","type":"date","label":"D","rules":[{"op":"notAfter","value":"today+6M"}]}""");

        Assert.True(Validate(definition, """{"d":"2027-04-30"}""").IsValid);
        Assert.False(Validate(definition, """{"d":"2027-05-01"}""").IsValid);
    }

    [Fact]
    public void Inactive_site_is_rejected()
    {
        var definition = Definition("""{"id":"site","type":"site","label":"Site"}""");

        Assert.True(Validate(definition, """{"site":"Riom"}""").IsValid);
        Assert.False(Validate(definition, """{"site":"riom"}""").IsValid);
    }
}
