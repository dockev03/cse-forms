using CseForms.Infrastructure.Distribution;

namespace CseForms.Infrastructure.Tests;

public class QrCodeRendererTests
{
    private const string Url = "https://formulaires.example.test/f/inscription?site=Riom";

    [Fact]
    public void Png_is_a_large_image_and_the_same_link_gives_the_same_code()
    {
        var renderer = new QrCodeRenderer();
        var png = renderer.Png(Url);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);

        // Largeur dans l'en-tête IHDR (octets 16 à 19, gros-boutiste) : au moins 500 pixels pour l'impression.
        var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        Assert.True(width >= 500, $"Largeur trop faible : {width} px.");
        Assert.Equal(png, renderer.Png(Url));
        Assert.NotEqual(png, renderer.Png("https://formulaires.example.test/f/inscription"));
    }

    [Fact]
    public void Svg_is_scalable_and_inert()
    {
        var svg = new QrCodeRenderer().Svg(Url);

        Assert.StartsWith("<svg", svg.TrimStart());
        Assert.Contains("viewBox", svg);
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" on", svg, StringComparison.OrdinalIgnoreCase);
    }
}
