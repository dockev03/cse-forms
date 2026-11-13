using CseForms.Domain.Common;

namespace CseForms.Domain.Forms;

/// <summary>
/// Modèle de formulaire enregistré par un administrateur (table <c>form_template</c>) à partir de la version en
/// ligne d'un formulaire. Il ne contient qu'un fichier de formulaire et son modèle PDF éventuel : ni réponse, ni
/// règle de notification, ni compte attribué, ni code d'accès. Il n'est pas modifiable : on le supprime et on en
/// enregistre un autre. Le supprimer ne change rien aux formulaires créés à partir de lui.
/// </summary>
public sealed class FormTemplate
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    private FormTemplate()
    {
        Name = null!;
        PackageJson = null!;
    }

    public FormTemplate(string name, string? description, string packageJson, string? pdfTemplateHtml, DateTimeOffset createdAt, string? createdBy)
    {
        Id = Guid.CreateVersion7();
        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
        if (string.IsNullOrWhiteSpace(packageJson))
        {
            throw new DomainException("Le fichier de formulaire du modèle est obligatoire.");
        }

        PackageJson = packageJson;
        PdfTemplateHtml = pdfTemplateHtml;
        CreatedAt = Utc.Require(createdAt, nameof(createdAt));
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }

    /// <summary>Nom affiché dans le catalogue des modèles.</summary>
    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Fichier de formulaire ; son adresse et son titre sont remplacés à chaque utilisation.</summary>
    public string PackageJson { get; private set; }

    public string? PdfTemplateHtml { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Auteur (<c>user:{id}</c>), sans email ni nom.</summary>
    public string? CreatedBy { get; private set; }

    public static string NormalizeName(string? name)
    {
        var value = string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (value.Length == 0)
        {
            throw new DomainException("Le nom du modèle est obligatoire.");
        }

        if (value.Length > NameMaxLength)
        {
            throw new DomainException($"Le nom du modèle dépasse {NameMaxLength} caractères.");
        }

        return value;
    }

    public static string? NormalizeDescription(string? description)
    {
        var value = string.Join(' ', (description ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (value.Length > DescriptionMaxLength)
        {
            throw new DomainException($"La description du modèle dépasse {DescriptionMaxLength} caractères.");
        }

        return value.Length == 0 ? null : value;
    }
}
