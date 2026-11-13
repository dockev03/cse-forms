using CseForms.Api.BackOffice;
using CseForms.Application.BackOffice;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

public sealed class SubmissionsModel(BackOfficeService service) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Statut { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Site { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Du { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Au { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tri { get; set; }

    /// <summary>Retour après une suppression : message de confirmation.</summary>
    [BindProperty(SupportsGet = true, Name = "supprime")]
    public string? Deleted { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public bool OldestFirst => Tri == "ancien";

    public FormHeader Form { get; private set; } = null!;

    public SubmissionPage Result { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid formId, CancellationToken cancellationToken)
    {
        var filter = new SubmissionFilter(Q, Statut, Site, Du, Au, OldestFirst, PageNumber);
        var result = await service.ListSubmissionsAsync(User.ToCurrentUser(), formId, filter, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        (Form, Result) = result.Value;
        return Page();
    }

    public string PageUrl(int page)
    {
        var query = FilterQuery();
        query.Add("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return $"/admin/formulaires/{Form.Id}/reponses{query}";
    }

    /// <summary>Export CSV avec les filtres en cours.</summary>
    public string ExportUrl() => $"/admin/formulaires/{Form.Id}/export{FilterQuery()}";

    private QueryBuilder FilterQuery()
    {
        var query = new QueryBuilder();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                query.Add(key, value);
            }
        }

        Add("q", Q);
        Add("statut", Statut);
        Add("site", Site);
        Add("du", Du?.ToString("yyyy-MM-dd"));
        Add("au", Au?.ToString("yyyy-MM-dd"));
        Add("tri", Tri);
        return query;
    }
}
