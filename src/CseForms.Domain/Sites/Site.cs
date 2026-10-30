using CseForms.Domain.Common;

namespace CseForms.Domain.Sites;

/// <summary>Site de l'entreprise, réutilisable dans tous les formulaires.</summary>
public sealed class Site
{
    public const int LabelMaxLength = 120;

    private Site()
    {
        Label = null!;
    }

    public Site(string label)
    {
        Id = Guid.CreateVersion7();
        Label = NormalizeLabel(label);
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string Label { get; private set; }

    public bool IsActive { get; private set; }

    public void Rename(string label) => Label = NormalizeLabel(label);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private static string NormalizeLabel(string label)
    {
        var value = (label ?? string.Empty).Trim();

        if (value.Length == 0 || value.Length > LabelMaxLength)
        {
            throw new DomainException($"Le libellé du site est obligatoire et limité à {LabelMaxLength} caractères.");
        }

        return value;
    }
}
