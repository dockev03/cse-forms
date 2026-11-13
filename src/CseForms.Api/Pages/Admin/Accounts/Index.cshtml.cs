using CseForms.Api.BackOffice;
using CseForms.Infrastructure.Identity;

namespace CseForms.Api.Pages.Admin.Accounts;

/// <summary>Liste des comptes des élus, réservée aux administrateurs.</summary>
public sealed class IndexModel(AccountAdministration accounts) : AdminPageModel
{
    public IReadOnlyList<AccountSummary> Accounts { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Accounts = await accounts.ListAsync(cancellationToken);
}
