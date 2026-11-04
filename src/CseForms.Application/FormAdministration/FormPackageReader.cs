using System.Text.Json;
using System.Text.Json.Nodes;
using CseForms.Application.FormDefinitions;

namespace CseForms.Application.FormAdministration;

/// <summary>
/// Lit un fichier de formulaire (par exemple <c>forms/inscription.json</c>) : réglages du formulaire
/// (slug, titre, préfixe, conservation, accès), présentation (<c>ui</c>) et définition (<c>sections</c>...).
/// Les réglages du formulaire ne sont pas recopiés dans la définition versionnée.
/// </summary>
public static class FormPackageReader
{
    private static readonly HashSet<string> FormSettingKeys = ["slug", "caseNumberPrefix", "retentionDays", "access", "ui"];

    private static readonly HashSet<string> DefinitionKeys = ["title", "sections", "receipt"];

    private static readonly HashSet<string> UiKeys = ["multiStep", "confirmationText"];

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 32 };

    public static FormPackageResult Read(string json)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: DocumentOptions) as JsonObject;
        }
        catch (JsonException)
        {
            return Fail("$", "JSON invalide.");
        }

        if (root is null)
        {
            return Fail("$", "Le fichier doit contenir un objet JSON.");
        }

        var errors = new List<DefinitionError>();
        foreach (var (key, _) in root)
        {
            if (key == "notifications")
            {
                errors.Add(new DefinitionError($"$.{key}", "Les destinataires des notifications se règlent en base, pas dans le fichier."));
            }
            else if (!FormSettingKeys.Contains(key) && !DefinitionKeys.Contains(key))
            {
                errors.Add(new DefinitionError($"$.{key}", "Propriété inconnue."));
            }
        }

        var slug = ReadString(root, "slug", required: true, errors);
        var title = ReadString(root, "title", required: true, errors);
        var prefix = ReadString(root, "caseNumberPrefix", required: false, errors);
        var retentionDays = ReadRetentionDays(root, errors);
        var accessMode = ReadAccessMode(root, errors);
        var uiJson = ReadUi(root, errors);

        var definition = new JsonObject();
        foreach (var (key, value) in root)
        {
            if (DefinitionKeys.Contains(key))
            {
                definition[key] = value?.DeepClone();
            }
        }

        var schemaJson = definition.ToJsonString();
        errors.AddRange(FormDefinitionParser.Parse(schemaJson).Errors);

        if (errors.Count > 0)
        {
            return new FormPackageResult(null, errors);
        }

        return new FormPackageResult(
            new FormPackage(slug!, title!, prefix, retentionDays, accessMode!.Value, schemaJson, uiJson),
            []);
    }

    private static string? ReadString(JsonObject root, string key, bool required, List<DefinitionError> errors)
    {
        var node = root[key];
        if (node is null)
        {
            if (required)
            {
                errors.Add(new DefinitionError($"$.{key}", "Propriété obligatoire."));
            }

            return null;
        }

        if (node.GetValueKind() != JsonValueKind.String || string.IsNullOrWhiteSpace(node.GetValue<string>()))
        {
            errors.Add(new DefinitionError($"$.{key}", "Texte non vide attendu."));
            return null;
        }

        return node.GetValue<string>().Trim();
    }

    private static int? ReadRetentionDays(JsonObject root, List<DefinitionError> errors)
    {
        var node = root["retentionDays"];
        if (node is null)
        {
            return null;
        }

        if (node.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue<int>(out var days) && days > 0)
        {
            return days;
        }

        errors.Add(new DefinitionError("$.retentionDays", "Nombre de jours entier et positif attendu."));
        return null;
    }

    private static AccessMode? ReadAccessMode(JsonObject root, List<DefinitionError> errors)
    {
        // Obligatoire : un formulaire ne devient jamais public par oubli.
        if (root["access"] is not JsonObject access
            || access.Count != 1
            || access["mode"] is not JsonValue mode
            || mode.GetValueKind() != JsonValueKind.String)
        {
            errors.Add(new DefinitionError("$.access", "Accès attendu : { \"mode\": \"public\" } ou { \"mode\": \"pin\" }."));
            return null;
        }

        switch (mode.GetValue<string>())
        {
            case "public":
                return AccessMode.Public;
            case "pin":
                return AccessMode.Pin;
            default:
                errors.Add(new DefinitionError("$.access.mode", "Mode d'accès inconnu : public ou pin."));
                return null;
        }
    }

    private static string? ReadUi(JsonObject root, List<DefinitionError> errors)
    {
        var node = root["ui"];
        if (node is null)
        {
            return null;
        }

        if (node is not JsonObject ui)
        {
            errors.Add(new DefinitionError("$.ui", "Objet attendu."));
            return null;
        }

        foreach (var (key, value) in ui)
        {
            var kind = value?.GetValueKind();
            var valid = key switch
            {
                "multiStep" => kind is JsonValueKind.True or JsonValueKind.False,
                "confirmationText" => kind == JsonValueKind.String,
                _ => false,
            };

            if (!valid)
            {
                errors.Add(new DefinitionError($"$.ui.{key}", UiKeys.Contains(key) ? "Type de valeur incorrect." : "Propriété inconnue."));
            }
        }

        return ui.ToJsonString();
    }

    private static FormPackageResult Fail(string path, string message) => new(null, [new DefinitionError(path, message)]);
}

public enum AccessMode
{
    Public,
    Pin,
}

/// <summary>Contenu d'un fichier de formulaire contrôlé. <c>SchemaJson</c> est la définition qui sera versionnée.</summary>
public sealed record FormPackage(
    string Slug,
    string Title,
    string? CaseNumberPrefix,
    int? RetentionDays,
    AccessMode AccessMode,
    string SchemaJson,
    string? UiJson);

public sealed record FormPackageResult(FormPackage? Package, IReadOnlyList<DefinitionError> Errors)
{
    public bool IsValid => Package is not null;
}
