using System.Text.Json;

namespace CseForms.Application.FormAdministration;

/// <summary>
/// Modèles de formulaires livrés avec l'application (<c>FormAdministration/Templates</c>, embarqués dans
/// l'assemblage) : un index <c>catalog.json</c> (code, nom, description) et un fichier de formulaire par code,
/// au format d'un import. Ils sont en lecture seule dans le back-office ; leur conformité est vérifiée par les tests.
/// </summary>
public static class BuiltInFormTemplates
{
    private const string ResourcePrefix = "CseForms.Templates.";

    private static readonly Lazy<IReadOnlyList<BuiltInFormTemplate>> Catalog = new(Load);

    public static IReadOnlyList<BuiltInFormTemplate> All => Catalog.Value;

    public static BuiltInFormTemplate? Find(string? code) =>
        All.SingleOrDefault(t => string.Equals(t.Code, code, StringComparison.Ordinal));

    private static IReadOnlyList<BuiltInFormTemplate> Load()
    {
        var entries = JsonSerializer.Deserialize<List<CatalogEntry>>(ReadResource("catalog.json"), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Index des modèles de formulaires illisible.");
        return entries
            .Select(e => new BuiltInFormTemplate(e.Code, e.Name, e.Description, ReadResource($"{e.Code}.json")))
            .ToList();
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(BuiltInFormTemplates).Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new InvalidOperationException($"Modèle de formulaire absent de l'application : {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record CatalogEntry(string Code, string Name, string Description);
}

/// <param name="Code">Identifiant stable, utilisé dans l'adresse de l'écran et le journal (<c>template=satisfaction</c>).</param>
public sealed record BuiltInFormTemplate(string Code, string Name, string Description, string PackageJson);
