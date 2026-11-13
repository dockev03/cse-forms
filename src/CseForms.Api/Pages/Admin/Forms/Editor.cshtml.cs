using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using CseForms.Api.BackOffice;
using CseForms.Application.FormAdministration;
using CseForms.Application.FormDefinitions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Éditeur visuel d'un formulaire : création (<c>/admin/formulaires/nouveau</c>, administrateurs) ou
/// modification (<c>/admin/formulaires/{id}/editeur</c>, administrateurs et éditeurs attribués). La page envoie
/// le fichier de formulaire en JSON, comme un import : il est enregistré comme brouillon après les mêmes
/// contrôles, puis l'aperçu s'ouvre. Sans script, le fichier se modifie directement en JSON.
/// </summary>
[RequestSizeLimit(MaxRequestBytes)]
public sealed class EditorModel(FormEditorService service) : PageModel
{
    /// <summary>Le fichier de formulaire, plus les champs du formulaire et l'enveloppe multipart.</summary>
    public const int MaxRequestBytes = FormImportService.MaxFileBytes + (64 * 1024);

    public EditorDocument Document { get; private set; } = null!;

    /// <summary>Fichier affiché : celui chargé, ou celui renvoyé par le navigateur s'il a été refusé.</summary>
    public string PackageJson { get; private set; } = string.Empty;

    public IReadOnlyList<DefinitionError> Errors { get; private set; } = [];

    public bool IsNew => Document.FormId is null;

    public string SourceName => Document.Source switch
    {
        EditorSource.Draft => "draft",
        EditorSource.Version => "version",
        _ => "new",
    };

    public async Task<IActionResult> OnGetAsync(Guid? formId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(formId, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        PackageJson = Indented(Document.PackageJson);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? formId, [FromForm(Name = "package")] string? package, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(package))
        {
            Errors = [new DefinitionError("$", "Le fichier de formulaire est vide.")];
        }
        else if (Encoding.UTF8.GetByteCount(package) > FormImportService.MaxFileBytes)
        {
            Errors = [new DefinitionError("$", $"Le fichier de formulaire dépasse {FormImportService.MaxFileBytes / 1024} Ko.")];
        }
        else
        {
            switch (await service.SaveAsync(User.ToCurrentUser(), formId, package, cancellationToken))
            {
                case EditorSaveResult.Saved saved:
                    return RedirectToPage("Preview", new { formId = saved.FormId, fait = "editeur" });
                case EditorSaveResult.Invalid invalid:
                    Errors = invalid.Errors;
                    break;
                case EditorSaveResult.NotFound:
                    return NotFound();
                default:
                    return Forbid();
            }
        }

        if (await LoadAsync(formId, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        PackageJson = package ?? string.Empty;
        return Page();
    }

    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>Fichier présenté indenté, lisible sans script ; laissé tel quel s'il n'est pas du JSON.</summary>
    private static string Indented(string json)
    {
        try
        {
            return JsonNode.Parse(json)?.ToJsonString(IndentedOptions) ?? json;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private async Task<IActionResult?> LoadAsync(Guid? formId, CancellationToken cancellationToken)
    {
        var user = User.ToCurrentUser();
        if (formId is not { } id)
        {
            if (await service.GetNewAsync(user, cancellationToken) is not { } blank)
            {
                return Forbid();
            }

            Document = blank;
            return null;
        }

        var result = await service.GetAsync(user, id, cancellationToken);
        switch (result.Status)
        {
            case LifecycleStatus.Ok:
                Document = result.Value!;
                return null;
            case LifecycleStatus.Forbidden:
                return Forbid();
            default:
                return NotFound();
        }
    }
}
