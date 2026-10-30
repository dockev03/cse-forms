namespace CseForms.Infrastructure.Persistence;

/// <summary>
/// Dernier rang attribué pour un préfixe et une année. Le compteur est tenu par préfixe, et non par
/// formulaire, pour que deux formulaires partageant un préfixe ne produisent jamais le même numéro.
/// </summary>
internal sealed class CaseNumberCounter
{
    public string Prefix { get; set; } = null!;

    public int Year { get; set; }

    public long LastValue { get; set; }
}
