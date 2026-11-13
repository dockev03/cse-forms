using CseForms.Application.Settings;

namespace CseForms.Application.Tests;

/// <summary>Notice RGPD complète : Markdown restreint, tout le reste échappé, liens limités à https et mailto.</summary>
public class PrivacyNoticeTests
{
    [Fact]
    public void Headings_paragraphs_lists_and_rule_are_rendered()
    {
        var html = PrivacyNotice.ToHtml("""
            ## Qui traite vos données ?
            Le CSE fictif,
            rue Fictive, 00000 Ville.

            ### Vos droits
            - accès
            - **rectification**

            1. Écrire au CSE
            2. Recevoir une réponse
            ---
            """);

        Assert.Equal(
            "<h2>Qui traite vos données ?</h2>\n" +
            "<p>Le CSE fictif, rue Fictive, 00000 Ville.</p>\n" +
            "<h3>Vos droits</h3>\n" +
            "<ul>\n<li>accès</li>\n<li><strong>rectification</strong></li>\n</ul>\n" +
            "<ol>\n<li>Écrire au CSE</li>\n<li>Recevoir une réponse</li>\n</ol>\n" +
            "<hr>\n",
            html);
    }

    [Fact]
    public void Table_has_a_header_row_and_scrolls_on_small_screens()
    {
        var html = PrivacyNotice.ToHtml("""
            | Catégorie | Données |
            | --- | --- |
            | Identité | Nom, prénom |
            """);

        Assert.Contains("<div class=\"table-wrap\"><table>", html, StringComparison.Ordinal);
        Assert.Contains("<th scope=\"col\">Catégorie</th><th scope=\"col\">Données</th>", html, StringComparison.Ordinal);
        Assert.Contains("<tr><td>Identité</td><td>Nom, prénom</td></tr>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_and_scripts_are_escaped_never_copied()
    {
        const string markdown = "<script>alert(1)</script> <img src=x onerror=alert(1)> <a href=\"javascript:alert(1)\">x</a>";

        var html = PrivacyNotice.ToHtml(markdown);

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a ", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Null(PrivacyNotice.Validate(markdown));
    }

    [Theory]
    [InlineData("[CNIL](https://www.cnil.fr/fr/plaintes)", "<a href=\"https://www.cnil.fr/fr/plaintes\" rel=\"noopener noreferrer\">CNIL</a>")]
    [InlineData("[le CSE](mailto:cse@example.test)", "<a href=\"mailto:cse@example.test\">le CSE</a>")]
    public void Https_and_mailto_links_are_kept(string markdown, string expected)
    {
        Assert.Null(PrivacyNotice.Validate(markdown));
        Assert.Contains(expected, PrivacyNotice.ToHtml(markdown), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[clic](javascript:alert(1))")]
    [InlineData("[clic](http://example.test)")]
    [InlineData("[clic](data:text/html,x)")]
    [InlineData("[clic](/admin)")]
    [InlineData("[clic](mailto:Nom <cse@example.test>)")]
    [InlineData("[clic](https://user@example.test)")]
    public void Other_links_are_refused_and_rendered_as_text(string markdown)
    {
        Assert.StartsWith("Ligne 1 : lien refusé", PrivacyNotice.Validate(markdown), StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", PrivacyNotice.ToHtml(markdown), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("# Titre", 1)]
    [InlineData("texte\n\n#### Titre", 3)]
    [InlineData("##Titre", 1)]
    public void Only_level_two_and_three_headings_are_accepted(string markdown, int line) =>
        Assert.StartsWith($"Ligne {line} : titre attendu", PrivacyNotice.Validate(markdown), StringComparison.Ordinal);

    [Fact]
    public void Table_without_separator_or_with_wrong_columns_is_refused()
    {
        Assert.StartsWith("Ligne 2 : un tableau", PrivacyNotice.Validate("| a | b |\n| c | d |"), StringComparison.Ordinal);
        Assert.StartsWith("Ligne 3 : 2 colonnes attendues", PrivacyNotice.Validate("| a | b |\n| --- | --- |\n| c |"), StringComparison.Ordinal);
    }

    [Fact]
    public void Setting_accepts_a_long_notice_but_not_beyond_its_limit_nor_invalid_syntax()
    {
        var longNotice = string.Join("\n\n", Enumerable.Repeat("Paragraphe fictif de la notice.", 400));
        Assert.True(longNotice.Length > SettingKeys.ValueMaxLength);

        Assert.Null(SettingKeys.Validate(SettingKeys.RgpdNotice, longNotice));
        Assert.NotNull(SettingKeys.Validate(SettingKeys.RgpdNotice, new string('a', PrivacyNotice.MaxLength + 1)));
        Assert.StartsWith("Ligne 1", SettingKeys.Validate(SettingKeys.RgpdNotice, "# Titre"), StringComparison.Ordinal);
        Assert.NotNull(SettingKeys.Validate(SettingKeys.CseName, longNotice));
    }

    [Fact]
    public void Notice_written_for_the_cse_is_valid()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", "rgpd", "notice-publique.md");
        Assert.Null(PrivacyNotice.Validate(File.ReadAllText(path)));
    }
}
