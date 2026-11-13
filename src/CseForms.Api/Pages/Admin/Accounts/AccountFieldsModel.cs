using CseForms.Infrastructure.Identity;

namespace CseForms.Api.Pages.Admin.Accounts;

/// <summary>Champs communs à la création et à la modification d'un compte.</summary>
public sealed record AccountFieldsModel(string DisplayName, string Role, IReadOnlyCollection<Guid> FormIds, IReadOnlyList<FormChoice> Forms);
