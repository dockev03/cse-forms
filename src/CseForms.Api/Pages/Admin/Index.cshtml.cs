using CseForms.Api.BackOffice;
using CseForms.Application.BackOffice;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin;

public sealed class IndexModel(BackOfficeService service) : PageModel
{
    public IReadOnlyList<FormSummary> Forms { get; private set; } = [];

    /// <summary>Réglages, QR code et flyer : administrateurs et éditeurs.</summary>
    public bool CanDistribute => !User.ToCurrentUser().IsReadOnly;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Forms = await service.ListFormsAsync(User.ToCurrentUser(), cancellationToken);

    public static string StatusLabel(string status) => status switch
    {
        "Draft" => "Brouillon",
        "Published" => "Publié",
        "Closed" => "Fermé",
        _ => status,
    };
}
