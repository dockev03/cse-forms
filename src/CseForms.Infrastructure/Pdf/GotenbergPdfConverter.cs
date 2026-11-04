using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using CseForms.Application.Pdf;

namespace CseForms.Infrastructure.Pdf;

public sealed class GotenbergOptions
{
    public const string Section = "Gotenberg";

    /// <summary>Adresse du conteneur Gotenberg sur le réseau interne, par exemple http://gotenberg:3000.</summary>
    public string Url { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>Conversion HTML vers PDF par Gotenberg (Chromium), format A4.</summary>
internal sealed class GotenbergPdfConverter(HttpClient http) : IPdfConverter
{
    // Format A4 et marges en pouces, comme l'attend Gotenberg.
    private static readonly Dictionary<string, string> Options = new()
    {
        ["paperWidth"] = "8.27",
        ["paperHeight"] = "11.7",
        ["marginTop"] = "0.55",
        ["marginBottom"] = "0.7",
        ["marginLeft"] = "0.6",
        ["marginRight"] = "0.6",
        ["printBackground"] = "true",
    };

    public async Task<byte[]> ConvertAsync(PdfHtml html, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        content.Add(HtmlFile(html.Body), "files", "index.html");
        content.Add(HtmlFile(html.Footer), "files", "footer.html");
        foreach (var (name, value) in Options)
        {
            content.Add(new StringContent(value, Encoding.UTF8), name);
        }

        using var response = await http.PostAsync("forms/chromium/convert/html", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Le corps de la réponse n'est pas repris : il pourrait citer le document.
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"Gotenberg a répondu {(int)response.StatusCode}."),
                inner: null,
                response.StatusCode);
        }

        var pdf = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (pdf.Length < 5 || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
        {
            throw new InvalidDataException("Réponse de Gotenberg qui n'est pas un PDF.");
        }

        return pdf;
    }

    private static ByteArrayContent HtmlFile(string html)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(html));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
        return file;
    }
}
