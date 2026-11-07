using System.Text.Json;
using System.Text.Json.Nodes;
using CseForms.Application.FormDefinitions;

namespace CseForms.Application.Notifications;

/// <summary>
/// Accusé de réception d'un formulaire (propriété <c>receipt</c> de la définition versionnée) :
/// champ email du répondant et texte fixe ajouté au message. L'accusé ne reprend jamais une donnée saisie.
/// </summary>
public sealed record ReceiptSettings(string EmailField, string? Message)
{
    public const int MessageMaxLength = 1000;

    /// <summary>Réglages de la définition ; null si le formulaire n'envoie pas d'accusé ou si <c>receipt</c> est invalide.</summary>
    public static ReceiptSettings? Read(string schemaJson)
    {
        var node = JsonNode.Parse(schemaJson)?["receipt"];
        return node is null || Check(node, definition: null).Count > 0
            ? null
            : new ReceiptSettings(node["emailField"]!.GetValue<string>(), node["message"]?.GetValue<string>().Trim());
    }

    /// <summary>
    /// Erreurs de la propriété <c>receipt</c>. Avec la définition, vérifie aussi que le champ désigné est un
    /// champ email de premier niveau.
    /// </summary>
    public static IReadOnlyList<DefinitionError> Check(JsonNode receipt, FormDefinition? definition)
    {
        const string Path = "$.receipt";
        var errors = new List<DefinitionError>();
        if (receipt is not JsonObject settings)
        {
            return [new DefinitionError(Path, "Objet attendu : { \"emailField\": \"...\" }.")];
        }

        foreach (var (key, value) in settings)
        {
            switch (key)
            {
                case "emailField" when value?.GetValueKind() == JsonValueKind.String:
                    break;
                case "message" when value?.GetValueKind() == JsonValueKind.String:
                    if (value.GetValue<string>().Trim().Length is 0 or > MessageMaxLength)
                    {
                        errors.Add(new DefinitionError($"{Path}.message", $"Texte de 1 à {MessageMaxLength} caractères attendu."));
                    }

                    break;
                case "emailField" or "message":
                    errors.Add(new DefinitionError($"{Path}.{key}", "Texte attendu."));
                    break;
                default:
                    errors.Add(new DefinitionError($"{Path}.{key}", "Propriété inconnue : l'accusé ne contient jamais de donnée saisie."));
                    break;
            }
        }

        if (settings["emailField"] is null)
        {
            errors.Add(new DefinitionError($"{Path}.emailField", "Propriété obligatoire."));
        }
        else if (definition is not null
            && settings["emailField"]!.GetValueKind() == JsonValueKind.String
            && !definition.AllTopLevelFields.Any(f => f.Id == settings["emailField"]!.GetValue<string>() && f.Type == FieldType.Email && f.Persist))
        {
            errors.Add(new DefinitionError($"{Path}.emailField", "Champ email enregistré, de premier niveau, attendu."));
        }

        return errors;
    }
}
