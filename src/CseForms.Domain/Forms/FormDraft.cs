using CseForms.Domain.Common;

namespace CseForms.Domain.Forms;

/// <summary>
/// Brouillon d'un formulaire (table <c>form_draft</c>) : fichier de formulaire et modèle PDF importés sans être
/// publiés, pour les vérifier dans l'aperçu. Un seul brouillon par formulaire, remplacé à chaque enregistrement.
/// Il n'est jamais servi au public ni rattaché à une réponse : le publier crée une version, comme un import.
/// </summary>
public sealed class FormDraft
{
    private FormDraft()
    {
        PackageJson = null!;
    }

    public FormDraft(Guid formId, string packageJson, string? pdfTemplateHtml, DateTimeOffset savedAt, string? savedBy)
    {
        FormId = formId;
        PackageJson = null!;
        Replace(packageJson, pdfTemplateHtml, savedAt, savedBy);
    }

    public Guid FormId { get; private set; }

    /// <summary>Fichier de formulaire tel qu'importé : réglages, présentation et définition, relus à l'aperçu et à la publication.</summary>
    public string PackageJson { get; private set; }

    public string? PdfTemplateHtml { get; private set; }

    public DateTimeOffset SavedAt { get; private set; }

    /// <summary>Auteur (<c>user:{id}</c>), sans email ni nom.</summary>
    public string? SavedBy { get; private set; }

    public void Replace(string packageJson, string? pdfTemplateHtml, DateTimeOffset savedAt, string? savedBy)
    {
        if (string.IsNullOrWhiteSpace(packageJson))
        {
            throw new DomainException("Le fichier de formulaire du brouillon est obligatoire.");
        }

        PackageJson = packageJson;
        PdfTemplateHtml = pdfTemplateHtml;
        SavedAt = Utc.Require(savedAt, nameof(savedAt));
        SavedBy = savedBy;
    }
}
