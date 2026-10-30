using System.Text.Json.Nodes;

namespace CseForms.Application.FormDefinitions;

/// <summary>
/// Résultat de la validation. <see cref="Data"/> ne contient que les champs visibles,
/// déclarés et enregistrables, normalisés : c'est ce qui est stocké dans la réponse.
/// </summary>
public sealed record SubmissionValidationResult(IReadOnlyList<ValidationError> Errors, JsonObject? Data)
{
    public bool IsValid => Errors.Count == 0;
}
