using CseForms.Application.Distribution;
using QRCoder;

namespace CseForms.Infrastructure.Distribution;

/// <summary>
/// QR codes avec QRCoder, niveau de correction Q (25 %) : lisible même abîmé ou mal imprimé.
/// </summary>
internal sealed class QrCodeRenderer : IQrCodeRenderer
{
    /// <summary>20 pixels par module : environ 600 à 900 pixels de côté, net à l'impression en A4.</summary>
    public const int PixelsPerModule = 20;

    public byte[] Png(string text)
    {
        using var data = Generate(text);
        return new PngByteQRCode(data).GetGraphic(PixelsPerModule);
    }

    public string Svg(string text)
    {
        using var data = Generate(text);
        return new SvgQRCode(data).GetGraphic(PixelsPerModule, "#000000", "#ffffff", drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    private static QRCodeData Generate(string text)
    {
        using var generator = new QRCodeGenerator();
        return generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
    }
}
