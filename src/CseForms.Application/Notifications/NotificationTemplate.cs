using System.Text.RegularExpressions;

namespace CseForms.Application.Notifications;

/// <summary>
/// Variables des notifications au CSE. Elles sont toutes non personnelles : aucune ne donne accès à une
/// valeur saisie autre que le site, que la spécification autorise.
/// </summary>
public static partial class NotificationTemplate
{
    public static readonly IReadOnlyList<string> Variables = ["formulaire", "numero", "date", "site", "lien"];

    public const string DefaultSubject = "Nouvelle réponse n° {numero} : {formulaire}";

    /// <summary>Variables inconnues d'un objet ou d'un corps, pour refuser une faute de frappe à l'enregistrement.</summary>
    public static IReadOnlyList<string> UnknownVariables(string? text) =>
        text is null
            ? []
            : VariablePattern().Matches(text).Select(m => m.Groups[1].Value).Where(v => !Variables.Contains(v)).Distinct().ToList();

    public static string Render(string text, IReadOnlyDictionary<string, string> values) =>
        VariablePattern().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);

    [GeneratedRegex(@"\{([^{}\s]+)\}")]
    private static partial Regex VariablePattern();
}
