using System.Text.Json.Nodes;

namespace CseForms.Application.FormDefinitions;

/// <summary>
/// Résultat de la validation. <see cref="Data"/> ne contient que les champs visibles,
/// déclarés et enregistrables, normalisés : c'est ce qui est stocké dans la réponse.
/// <see cref="Ending"/> est la page de fin atteinte par un saut, null pour la confirmation standard.
/// </summary>
public sealed record SubmissionValidationResult(IReadOnlyList<ValidationError> Errors, JsonObject? Data, string? Ending = null)
{
    public bool IsValid => Errors.Count == 0;
}
