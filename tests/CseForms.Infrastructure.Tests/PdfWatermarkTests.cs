using System.Text;
using CseForms.Application.Pdf;
using CseForms.Infrastructure.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace CseForms.Infrastructure.Tests;

/// <summary>Filigrane posé sur un PDF existant : pages conservées, texte en Helvetica, aucune police à embarquer.</summary>
public class PdfWatermarkTests
{
    private static readonly PdfWatermarkText Text = new("Confidentiel CSE", "Téléchargé par Prénom (Fictif) le 13/11/2026");

    private static byte[] TwoPagePdf()
    {
        using var document = new PdfDocument();
        document.AddPage();
        document.AddPage().Width = PdfSharp.Drawing.XUnit.FromPoint(842); // paysage
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static string AllContent(PdfPage page) =>
        Encoding.Latin1.GetString(page.Contents.Elements.Items
            .Select(item => item is PdfSharp.Pdf.Advanced.PdfReference reference ? reference.Value : item)
            .OfType<PdfDictionary>()
            .SelectMany(content => content.Stream.UnfilteredValue)
            .ToArray());

    [Fact]
    public void Every_page_gets_the_watermark_over_its_isolated_content()
    {
        var watermarked = new PdfSharpWatermarker().Apply(TwoPagePdf(), Text);

        using var document = PdfReader.Open(new MemoryStream(watermarked), PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
        foreach (var page in document.Pages)
        {
            var content = AllContent(page);
            Assert.StartsWith("q\n", content, StringComparison.Ordinal);
            Assert.Contains("(CONFIDENTIEL CSE) Tj", content, StringComparison.Ordinal);
            Assert.Contains("(Téléchargé par Prénom \\(Fictif\\) le 13/11/2026) Tj", content, StringComparison.Ordinal);
            Assert.Contains("/CseWatermarkState gs", content, StringComparison.Ordinal);

            var font = page.Resources.Elements.GetDictionary("/Font")!.Elements.GetDictionary("/CseWatermarkFont")!;
            Assert.Equal("/Helvetica-Bold", font.Elements.GetName("/BaseFont"));
            Assert.Equal("/WinAnsiEncoding", font.Elements.GetName("/Encoding"));
        }
    }

    [Fact]
    public void Unreadable_file_is_refused_rather_than_delivered_without_watermark() =>
        Assert.ThrowsAny<Exception>(() => new PdfSharpWatermarker().Apply("%PDF-fictif"u8.ToArray(), Text));

    [Theory]
    [InlineData("Élodie Œuvre", "Élodie \u008cuvre")]
    [InlineData("Łukasz Dąbrowski", "Lukasz Dabrowski")]
    [InlineData("Nom\u0007\nFictif", "NomFictif")]
    [InlineData("名前", "??")]
    public void Name_is_encoded_in_win_ansi_with_a_readable_fallback(string value, string expected) =>
        Assert.Equal(expected, Encoding.Latin1.GetString(PdfSharpWatermarker.WinAnsi(value)));
}
