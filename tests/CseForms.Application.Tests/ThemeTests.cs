using CseForms.Application.Settings;

namespace CseForms.Application.Tests;

/// <summary>Personnalisation du formulaire public : couleur contrôlée (RGAA), feuille de style sans valeur douteuse.</summary>
public class ThemeTests
{
    private const string Background = "data:image/png;base64,iVBORw0KGgoAAQ==";

    [Theory]
    [InlineData("#000000", "#ffffff", 21.0)]
    [InlineData("#ffffff", "#ffffff", 1.0)]
    [InlineData("#767676", "#ffffff", 4.54)]
    [InlineData("#1d4f91", "#ffffff", 8.14)]
    public void Contrast_ratio_follows_wcag(string first, string second, double expected) =>
        Assert.Equal(expected, ThemeColor.ContrastRatio(first, second), precision: 2);

    [Theory]
    [InlineData("#1D4F91", "#1d4f91")]
    [InlineData("#03A", "#0033aa")]
    [InlineData("rouge", "rouge")]
    public void Color_is_normalized(string value, string expected) =>
        Assert.Equal(expected, SettingKeys.Normalize(SettingKeys.ThemePrimaryColor, value));

    [Theory]
    [InlineData(ThemeColor.Default)]
    [InlineData("#000000")]
    [InlineData("#a3004c")]
    public void Dark_enough_color_is_accepted(string color) =>
        Assert.Null(SettingKeys.Validate(SettingKeys.ThemePrimaryColor, color));

    [Theory]
    [InlineData("#ffcc00")]
    [InlineData("#767676")] // 4,54:1 sur blanc, mais moins de 4,5:1 sur le fond de page
    [InlineData("#3399ff")]
    public void Light_color_is_refused_with_its_ratio(string color)
    {
        var error = SettingKeys.Validate(SettingKeys.ThemePrimaryColor, color);

        Assert.NotNull(error);
        Assert.Contains("Contraste insuffisant", error);
        Assert.Contains("4,5:1", error);
    }

    [Theory]
    [InlineData("rouge")]
    [InlineData("#12345")]
    [InlineData("#1d4f91; } body { display: none")]
    [InlineData("rgb(0, 0, 0)")]
    public void Malformed_color_is_refused(string color) =>
        Assert.Contains("#rrggbb", SettingKeys.Validate(SettingKeys.ThemePrimaryColor, color));

    [Fact]
    public void Darker_variant_keeps_or_improves_the_contrast()
    {
        Assert.Equal("#173f74", ThemeColor.Darken("#1d4f91"));
        Assert.True(ThemeColor.ContrastRatio(ThemeColor.Darken("#1d4f91"), "#ffffff") > ThemeColor.ContrastRatio("#1d4f91", "#ffffff"));
    }

    [Fact]
    public void Background_is_a_png_or_jpeg_of_512_kb_at_most()
    {
        Assert.Null(SettingKeys.Validate(SettingKeys.ThemeBackground, Background));
        Assert.NotNull(SettingKeys.Validate(SettingKeys.ThemeBackground, "data:image/svg+xml;base64,PHN2Zz4="));
        Assert.Null(BackgroundImage.Image.ToDataUri(new byte[BackgroundImage.MaxBytes + 1]));
        Assert.Null(CseLogo.ToDataUri([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[CseLogo.MaxBytes]]));
        Assert.NotNull(BackgroundImage.Image.ToDataUri([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[CseLogo.MaxBytes]]));
    }

    [Fact]
    public void Valid_image_is_decoded_to_its_bytes_and_type_only()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

        var decoded = CseLogo.Image.Decode(CseLogo.ToDataUri(png));

        Assert.NotNull(decoded);
        Assert.Equal(png, decoded.Value.Content);
        Assert.Equal("image/png", decoded.Value.MediaType);
        Assert.Null(CseLogo.Image.Decode(null));
        Assert.Null(CseLogo.Image.Decode("data:image/svg+xml;base64,PHN2Zz4="));
    }

    [Fact]
    public void Stylesheet_is_empty_without_settings()
    {
        var css = ThemeStylesheet.Render(new Dictionary<string, string>());

        Assert.DoesNotContain("--primary", css);
        Assert.DoesNotContain("background-image", css);
    }

    [Fact]
    public void Stylesheet_applies_color_and_background_on_a_plain_form()
    {
        var css = ThemeStylesheet.Render(new Dictionary<string, string>
        {
            [SettingKeys.ThemePrimaryColor] = "#a3004c",
            [SettingKeys.ThemeBackground] = Background,
        });

        Assert.Contains(":root { --primary: #a3004c; --primary-dark: #82003c; }", css);
        Assert.Contains($"background-image: url(\"{Background}\")", css);
        Assert.Contains(".page { background: var(--bg);", css);
    }

    [Fact]
    public void Stylesheet_ignores_invalid_values_stored_in_database()
    {
        var css = ThemeStylesheet.Render(new Dictionary<string, string>
        {
            [SettingKeys.ThemePrimaryColor] = "#ffcc00",
            [SettingKeys.ThemeBackground] = "data:image/png;base64,AAAA\"); } body { background: url(\"https://example.test/x.png",
        });

        Assert.DoesNotContain("--primary", css);
        Assert.DoesNotContain("url(", css);
    }
}
