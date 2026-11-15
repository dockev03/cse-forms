using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CseForms.Application.FormDefinitions;
using CseForms.Application.Notifications;
using CseForms.Application.Pdf;

namespace CseForms.Application.FormAdministration;

/// <summary>
/// Lit un fichier de formulaire (par exemple <c>forms/inscription.json</c>) : réglages du formulaire
/// (slug, titre, préfixe, conservation, accès), présentation (<c>ui</c>) et définition (<c>sections</c>, <c>endings</c>...).
/// Les réglages du formulaire ne sont pas recopiés dans la définition versionnée. Le modèle PDF
/// (<c>pdfTemplate</c>) est un fichier HTML voisin, versionné avec la définition et contrôlé contre elle.
/// </summary>
public static partial class FormPackageReader
{
    private static readonly HashSet<string> FormSettingKeys = ["slug", "caseNumberPrefix", "retentionDays", "access", "ui", "pdfTemplate"];

    private static readonly HashSet<string> DefinitionKeys = ["title", "sections", "endings", "receipt"];

    private static readonly HashSet<string> UiKeys = ["multiStep", "confirmationText"];

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 32 };

    /// <param name="readCompanionFile">
    /// Lit un fichier voisin du fichier de formulaire par son nom (sans chemin) ; null s'il n'existe pas.
    /// </param>
    public static FormPackageResult Read(string json, Func<string, string?>? readCompanionFile = null)
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
        var parsed = FormDefinitionParser.Parse(schemaJson);
        errors.AddRange(parsed.Errors);

        if (root["receipt"] is { } receipt)
        {
            errors.AddRange(ReceiptSettings.Check(receipt, parsed.IsValid ? parsed.Definition : null));
        }

        var pdfTemplate = ReadPdfTemplate(root, readCompanionFile, parsed.Definition, errors);

        if (errors.Count > 0)
        {
            return new FormPackageResult(null, errors);
        }

        return new FormPackageResult(
            new FormPackage(slug!, title!, prefix, retentionDays, accessMode!.Value, schemaJson, uiJson, pdfTemplate),
            []);
    }

    private static string? ReadPdfTemplate(
        JsonObject root,
        Func<string, string?>? readCompanionFile,
        FormDefinition? definition,
        List<DefinitionError> errors)
    {
        const string TemplatePath = "$.pdfTemplate";
        var node = root["pdfTemplate"];
        if (node is null)
        {
            return null;
        }

        // Un simple nom de fichier : jamais de chemin, pour ne rien lire hors du dossier du formulaire.
        if (node.GetValueKind() != JsonValueKind.String || !TemplateFileNamePattern().IsMatch(node.GetValue<string>()))
        {
            errors.Add(new DefinitionError(TemplatePath, "Nom de fichier HTML attendu, sans chemin : inscription.pdf.html."));
            return null;
        }

        var content = readCompanionFile?.Invoke(node.GetValue<string>());
        if (content is null)
        {
            errors.Add(new DefinitionError(TemplatePath, "Fichier du modèle PDF introuvable à côté du fichier de formulaire."));
            return null;
        }

        if (definition is null)
        {
            // La définition est déjà signalée invalide : le modèle ne peut pas être contrôlé contre elle.
            return null;
        }

        var template = PdfTemplate.Parse(content, definition);
        errors.AddRange(template.Errors.Select(message => new DefinitionError(TemplatePath, message)));
        return template.IsValid ? content : null;
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

    [GeneratedRegex(@"^[a-z0-9][a-z0-9_-]*(\.[a-z0-9_-]+)*\.html\z")]
    private static partial Regex TemplateFileNamePattern();
}

public enum AccessMode
{
    Public,
    Pin,
}

/// <summary>
/// Contenu d'un fichier de formulaire contrôlé. <c>SchemaJson</c>, <c>UiJson</c> et <c>PdfTemplateHtml</c>
/// sont figés dans la version publiée.
/// </summary>
public sealed record FormPackage(
    string Slug,
    string Title,
    string? CaseNumberPrefix,
    int? RetentionDays,
    AccessMode AccessMode,
    string SchemaJson,
    string? UiJson,
    string? PdfTemplateHtml = null);

public sealed record FormPackageResult(FormPackage? Package, IReadOnlyList<DefinitionError> Errors)
{
    public bool IsValid => Package is not null;
}
