namespace CseForms.Application.FormDefinitions;

/// <summary>
/// Données externes nécessaires à la validation.
/// <paramref name="Today"/> est la date du jour dans le fuseau du CSE (pour <c>today</c>, <c>past</c>, <c>future</c>).
/// <paramref name="ActiveSites"/> contient les libellés des sites actifs, lus en base.
/// </summary>
public sealed record ValidationContext(DateOnly Today, IReadOnlyCollection<string> ActiveSites);
