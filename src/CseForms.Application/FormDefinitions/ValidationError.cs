namespace CseForms.Application.FormDefinitions;

/// <summary>
/// Erreur de saisie renvoyée au répondant. Le message ne reprend jamais la valeur saisie.
/// Le chemin suit la forme <c>enfants[0].naissance</c> ou <c>adresse.codePostal</c>.
/// </summary>
public sealed record ValidationError(string Path, string Code, string Message);

public static class ValidationCodes
{
    public const string Required = "required";
    public const string InvalidType = "invalidType";
    public const string TooShort = "tooShort";
    public const string TooLong = "tooLong";
    public const string InvalidFormat = "invalidFormat";
    public const string InvalidEmail = "invalidEmail";
    public const string InvalidPhone = "invalidPhone";
    public const string InvalidDate = "invalidDate";
    public const string InvalidPostalCode = "invalidPostalCode";
    public const string InvalidOption = "invalidOption";
    public const string DuplicateOption = "duplicateOption";
    public const string TooFewItems = "tooFewItems";
    public const string TooManyItems = "tooManyItems";
    public const string MustBeAccepted = "mustBeAccepted";

    public static string Rule(RuleOperator op) => op switch
    {
        RuleOperator.After => "rule.after",
        RuleOperator.Before => "rule.before",
        RuleOperator.NotBefore => "rule.notBefore",
        RuleOperator.NotAfter => "rule.notAfter",
        RuleOperator.Past => "rule.past",
        RuleOperator.Future => "rule.future",
        _ => "rule.sameAs",
    };
}

internal static class ValidationMessages
{
    public static string For(string code, string label, string? detail = null) => code switch
    {
        ValidationCodes.Required => $"« {label} » est obligatoire.",
        ValidationCodes.InvalidType => $"« {label} » : valeur non reconnue.",
        ValidationCodes.TooShort => $"« {label} » doit contenir au moins {detail} caractères.",
        ValidationCodes.TooLong => $"« {label} » ne doit pas dépasser {detail} caractères.",
        ValidationCodes.InvalidFormat => $"« {label} » n'a pas le format attendu.",
        ValidationCodes.InvalidEmail => $"« {label} » n'est pas une adresse email valide.",
        ValidationCodes.InvalidPhone => $"« {label} » n'est pas un numéro de téléphone valide.",
        ValidationCodes.InvalidDate => $"« {label} » n'est pas une date valide.",
        ValidationCodes.InvalidPostalCode => $"« {label} » doit comporter 5 chiffres.",
        ValidationCodes.InvalidOption or ValidationCodes.DuplicateOption => $"« {label} » : choix non autorisé.",
        ValidationCodes.TooFewItems => $"« {label} » : au moins {detail} élément(s) attendu(s).",
        ValidationCodes.TooManyItems => $"« {label} » : {detail} élément(s) au maximum.",
        ValidationCodes.MustBeAccepted => $"« {label} » doit être coché.",
        "rule.after" => $"« {label} » doit être postérieure à {detail}.",
        "rule.before" => $"« {label} » doit être antérieure à {detail}.",
        "rule.notBefore" => $"« {label} » ne peut pas être antérieure à {detail}.",
        "rule.notAfter" => $"« {label} » ne peut pas être postérieure à {detail}.",
        "rule.past" => $"« {label} » doit être dans le passé.",
        "rule.future" => $"« {label} » doit être dans le futur.",
        "rule.sameAs" => $"« {label} » doit être identique à « {detail} ».",
        _ => $"« {label} » est invalide.",
    };
}
