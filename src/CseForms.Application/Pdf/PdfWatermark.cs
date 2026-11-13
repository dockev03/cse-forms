using System.Globalization;
using CseForms.Application.Common;

namespace CseForms.Application.Pdf;

/// <summary>
/// Filigrane apposé à chaque téléchargement du PDF d'une réponse, quand le formulaire le demande :
/// « Confidentiel CSE », puis « Téléchargé par X le JJ/MM/AAAA » (heure de Paris). Le PDF stocké reste intact.
/// </summary>
public sealed record PdfWatermarkText(string Title, string Detail)
{
    public const string ConfidentialTitle = "Confidentiel CSE";

    public static PdfWatermarkText For(string downloadedBy, DateTimeOffset nowUtc) =>
        new(ConfidentialTitle, $"Téléchargé par {downloadedBy} le {CseCalendar.ToLocal(nowUtc).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}");
}

public interface IPdfWatermarker
{
    /// <summary>Copie du PDF avec le filigrane sur chaque page ; lève une exception si le PDF est illisible.</summary>
    byte[] Apply(byte[] pdf, PdfWatermarkText text);
}
