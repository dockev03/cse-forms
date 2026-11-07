namespace CseForms.Application.Distribution;

/// <summary>QR code du lien public d'un formulaire.</summary>
public interface IQrCodeRenderer
{
    /// <summary>Image PNG haute définition (impression).</summary>
    byte[] Png(string text);

    /// <summary>Image vectorielle SVG, sans script ni lien, redimensionnable.</summary>
    string Svg(string text);
}

public interface IDistributionStore
{
    Task<DistributionForm?> GetFormAsync(Guid formId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetActiveSiteLabelsAsync(CancellationToken cancellationToken);
}

/// <summary>Ce que la diffusion doit savoir d'un formulaire ; le haché du code sert seulement à vérifier la saisie.</summary>
public sealed record DistributionForm(Guid Id, string Slug, string Title, string Status, string? CurrentSchemaJson, string? AccessCodeHash);

public enum QrFormat
{
    Png,
    Svg,
}

public enum DistributionStatus
{
    Ok,
    NotFound,
    Forbidden,
    Invalid,
}

public sealed record DistributionFile(string FileName, string ContentType, byte[] Content);

/// <summary>Résultat d'une action de diffusion ; <c>Error</c> est un message en français pour l'élu.</summary>
public sealed record DistributionResult<T>(DistributionStatus Status, T? Value = default, string? Error = null)
    where T : class
{
    public static DistributionResult<T> Ok(T value) => new(DistributionStatus.Ok, value);

    public static DistributionResult<T> Invalid(string error) => new(DistributionStatus.Invalid, Error: error);
}

/// <summary>Écran de diffusion : lien public, sites proposés pour un QR code par site, code d'accès requis.</summary>
public sealed record DistributionPage(
    Guid FormId,
    string Slug,
    string Title,
    string Status,
    string? PublicUrl,
    string? SiteParameter,
    IReadOnlyList<string> Sites,
    bool RequiresAccessCode);
