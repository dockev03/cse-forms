namespace CseForms.Application.FormDefinitions;

/// <summary>Erreur dans une définition de formulaire, destinée à l'éditeur du formulaire.</summary>
public sealed record DefinitionError(string Path, string Message);

public sealed record DefinitionParseResult(FormDefinition? Definition, IReadOnlyList<DefinitionError> Errors)
{
    public bool IsValid => Definition is not null && Errors.Count == 0;
}
