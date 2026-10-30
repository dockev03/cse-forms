namespace CseForms.Application.FormDefinitions;

/// <summary>Limites appliquées même si la définition n'en précise pas, pour borner la taille des réponses.</summary>
public static class FieldLimits
{
    public const int TextDefaultMaxLength = 200;
    public const int TextareaDefaultMaxLength = 4000;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 30;
    public const int PostalCodeLength = 5;

    public static int MaxLengthCeiling(FieldType type) => type == FieldType.Textarea ? 20000 : 1000;

    public static int DefaultMaxLength(FieldType type) => type switch
    {
        FieldType.Textarea => TextareaDefaultMaxLength,
        FieldType.Email => EmailMaxLength,
        FieldType.Phone => PhoneMaxLength,
        _ => TextDefaultMaxLength,
    };
}
