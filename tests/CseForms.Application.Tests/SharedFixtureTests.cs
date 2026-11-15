using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CseForms.Application.FormDefinitions;

namespace CseForms.Application.Tests;

/// <summary>
/// Exécute les jeux de tests partagés de <c>tests/fixtures/form-engine</c>.
/// Le moteur client devra passer les mêmes cas.
/// </summary>
public class SharedFixtureTests
{
    private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures", "form-engine");

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var file in Directory.GetFiles(FixtureDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            var fixture = JsonNode.Parse(File.ReadAllText(file))!;
            foreach (var item in fixture["cases"]!.AsArray())
            {
                data.Add(Path.GetFileName(file), item!["name"]!.GetValue<string>());
            }
        }

        return data;
    }

    [Fact]
    public void Fixtures_are_present() => Assert.NotEmpty(Directory.GetFiles(FixtureDirectory, "*.json"));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case_matches_expected_result(string file, string name)
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, file)))!;
        var parse = FormDefinitionParser.Parse(fixture["definition"]!.ToJsonString());
        Assert.True(parse.IsValid, string.Join(Environment.NewLine, parse.Errors));

        var context = new ValidationContext(
            DateOnly.ParseExact(fixture["today"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            fixture["sites"]!.AsArray().Select(s => s!.GetValue<string>()).ToArray());

        var testCase = fixture["cases"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == name)!;
        using var input = JsonDocument.Parse(testCase["input"]!.ToJsonString());

        var result = SubmissionValidator.Validate(parse.Definition!, input.RootElement, context);

        var expectedErrors = testCase["errors"]!.AsArray()
            .Select(e => $"{e!["path"]!.GetValue<string>()} {e["code"]!.GetValue<string>()}")
            .Order(StringComparer.Ordinal);
        var actualErrors = result.Errors.Select(e => $"{e.Path} {e.Code}").Order(StringComparer.Ordinal);
        Assert.Equal(expectedErrors, actualErrors);

        if (testCase["data"] is { } expectedData)
        {
            Assert.True(
                JsonNode.DeepEquals(expectedData, result.Data),
                $"Attendu : {expectedData.ToJsonString()}{Environment.NewLine}Obtenu : {result.Data?.ToJsonString()}");
        }

        // « ending » : page de fin attendue (null pour la confirmation standard) ; non vérifiée si absente.
        if (testCase.AsObject().TryGetPropertyValue("ending", out var expectedEnding))
        {
            Assert.Equal(expectedEnding?.GetValue<string>(), result.Ending);
        }
    }
}
