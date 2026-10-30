using CseForms.Domain.Common;

namespace CseForms.Domain.Forms;

/// <summary>
/// Définition figée d'un formulaire à un instant donné. Une version n'est jamais modifiée :
/// publier une modification crée une nouvelle version.
/// </summary>
public sealed class FormVersion
{
    private FormVersion()
    {
        SchemaJson = null!;
    }

    internal FormVersion(
        Guid formId,
        int number,
        string schemaJson,
        string? uiJson,
        string? pdfTemplateHtml,
        DateTimeOffset createdAt,
        string? createdBy)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            throw new DomainException("La définition du formulaire est obligatoire.");
        }

        Id = Guid.CreateVersion7();
        FormId = formId;
        Number = number;
        SchemaJson = schemaJson;
        UiJson = uiJson;
        PdfTemplateHtml = pdfTemplateHtml;
        CreatedAt = Utc.Require(createdAt, nameof(createdAt));
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }

    public Guid FormId { get; private set; }

    public int Number { get; private set; }

    public string SchemaJson { get; private set; }

    public string? UiJson { get; private set; }

    public string? PdfTemplateHtml { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? CreatedBy { get; private set; }
}
