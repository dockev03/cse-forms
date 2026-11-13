using System.Text;
using CseForms.Api.BackOffice;
using CseForms.Application.BackOffice;
using CseForms.Application.FormAdministration;
using CseForms.Application.FormDefinitions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CseForms.Api.Pages.Admin.Forms;

/// <summary>
/// Import d'un fichier de formulaire (JSON) et de son modèle PDF éventuel : création (administrateurs) ou
/// nouvelle version d'un formulaire attribué (administrateurs et éditeurs). Rien n'est écrit sur le disque :
/// les fichiers sont lus en mémoire, limités en taille, puis contrôlés comme par <c>forms import</c>.
/// </summary>
[RequestSizeLimit(MaxRequestBytes)]
public sealed class ImportModel(FormImportService service, FormDraftService drafts, IAuditLog audit) : PageModel
{
    /// <summary>Deux fichiers au plus, plus les champs du formulaire et l'enveloppe multipart.</summary>
    public const int MaxRequestBytes = (2 * FormImportService.MaxFileBytes) + (64 * 1024);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public IReadOnlyList<DefinitionError> Errors { get; private set; } = [];

    public FormImportResult.Done? Result { get; private set; }

    public bool AskAccessCode { get; private set; }

    public bool AskConfirmPublic { get; private set; }

    public bool IsAdmin => User.ToCurrentUser().IsAdmin;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var user = User.ToCurrentUser();
        if (user.IsReadOnly)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, null, "form-import", cancellationToken);
            return Forbid();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        IFormFile? definition,
        IFormFile? pdfTemplate,
        [FromForm] string? code,
        [FromForm] string? confirmation,
        [FromForm] bool confirmPublic,
        CancellationToken cancellationToken)
    {
        var json = await ReadTextAsync(definition, "definition", "Le fichier de formulaire", required: true, cancellationToken);
        var template = await ReadTextAsync(pdfTemplate, "pdfTemplate", "Le modèle PDF", required: false, cancellationToken);
        if (Errors.Count > 0)
        {
            return Page();
        }

        var result = await service.ImportAsync(
            User.ToCurrentUser(),
            new FormImportRequest(json!, template, code, confirmation, confirmPublic),
            cancellationToken);

        switch (result)
        {
            case FormImportResult.Forbidden:
                return Forbid();
            case FormImportResult.Done done:
                Result = done;
                break;
            case FormImportResult.Invalid invalid:
                Errors = invalid.Errors;
                break;
            case FormImportResult.AccessCodeRequired:
                AskAccessCode = true;
                Errors = [new DefinitionError("code", "Ce formulaire est protégé par un code d'accès, pas encore défini : saisissez-le, puis renvoyez le fichier.")];
                break;
            case FormImportResult.ConfirmPublicRequired:
                AskConfirmPublic = true;
                Errors = [new DefinitionError("$.access", "Ce fichier rend public un formulaire protégé par un code d'accès : cochez la confirmation, puis renvoyez le fichier.")];
                break;
        }

        return Page();
    }

    /// <summary>
    /// Enregistre les fichiers comme brouillon, sans rien publier, puis ouvre l'aperçu. Le code d'accès et la
    /// confirmation du passage en public sont demandés à la publication du brouillon : ils sont ignorés ici.
    /// </summary>
    public async Task<IActionResult> OnPostDraftAsync(IFormFile? definition, IFormFile? pdfTemplate, CancellationToken cancellationToken)
    {
        var json = await ReadTextAsync(definition, "definition", "Le fichier de formulaire", required: true, cancellationToken);
        var template = await ReadTextAsync(pdfTemplate, "pdfTemplate", "Le modèle PDF", required: false, cancellationToken);
        if (Errors.Count > 0)
        {
            return Page();
        }

        switch (await drafts.SaveAsync(User.ToCurrentUser(), json!, template, cancellationToken))
        {
            case DraftSaveResult.Saved saved:
                return RedirectToPage("Preview", new { formId = saved.FormId, fait = "brouillon" });
            case DraftSaveResult.Invalid invalid:
                Errors = invalid.Errors;
                return Page();
            default:
                return Forbid();
        }
    }

    public static string RetentionLabel(int? days) => days is { } d ? $"{d} jours" : "aucune purge";

    /// <summary>Texte UTF-8 (BOM toléré) ; null si absent. Les erreurs ne reprennent jamais le contenu.</summary>
    private async Task<string?> ReadTextAsync(IFormFile? file, string path, string name, bool required, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            if (required)
            {
                AddError(path, $"{name} est obligatoire.");
            }

            return null;
        }

        if (file.Length > FormImportService.MaxFileBytes)
        {
            AddError(path, $"{name} dépasse {FormImportService.MaxFileBytes / 1024} Ko.");
            return null;
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var start = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        try
        {
            return StrictUtf8.GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            AddError(path, $"{name} n'est pas un fichier texte UTF-8.");
            return null;
        }
    }

    private void AddError(string path, string message) => Errors = [.. Errors, new DefinitionError(path, message)];
}
