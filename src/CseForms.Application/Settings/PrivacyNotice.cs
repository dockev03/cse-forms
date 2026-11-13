using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace CseForms.Application.Settings;

/// <summary>
/// Notice d'information complète sur les données personnelles (paramètre <c>rgpd.notice</c>), publiée sur
/// <see cref="PublicPath"/>. Rédigée en Markdown restreint et convertie ici en HTML : tout le texte est échappé,
/// seules les constructions listées dans <see cref="Syntax"/> produisent des balises, et les liens n'acceptent
/// que <c>https:</c> et <c>mailto:</c>. Aucun HTML saisi n'est jamais recopié tel quel.
/// </summary>
public static partial class PrivacyNotice
{
    public const string PublicPath = "/donnees-personnelles";

    /// <summary>Plus long qu'un paramètre ordinaire : une notice complète fait quelques pages.</summary>
    public const int MaxLength = 20000;

    public const string Syntax =
        "titres « ## » et « ### », paragraphes séparés par une ligne vide, listes (« - » ou « 1. »), tableaux « | a | b | », " +
        "**gras**, liens [texte](https://…) ou [texte](mailto:…), ligne « --- »";

    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>Première erreur de syntaxe, avec son numéro de ligne ; null si la notice est acceptée.</summary>
    public static string? Validate(string markdown) => Convert(markdown).Error;

    /// <summary>HTML de la notice. Une construction refusée (lien non autorisé) est rendue comme du texte.</summary>
    public static string ToHtml(string markdown) => Convert(markdown).Html;

    private static (string Html, string? Error) Convert(string markdown)
    {
        var lines = (markdown ?? string.Empty).ReplaceLineEndings("\n").Split('\n');
        var html = new StringBuilder();
        string? error = null;
        var paragraph = new List<string>();
        var paragraphLine = 0;
        string? list = null;
        List<string[]>? table = null;
        var tableLine = 0;

        void Fail(int line, string message) => error ??= $"Ligne {line.ToString(CultureInfo.InvariantCulture)} : {message}";

        void CloseParagraph()
        {
            if (paragraph.Count > 0)
            {
                html.Append("<p>").Append(Inline(string.Join(' ', paragraph), paragraphLine, Fail)).Append("</p>\n");
                paragraph.Clear();
            }
        }

        void CloseList()
        {
            if (list is not null)
            {
                html.Append("</").Append(list).Append(">\n");
                list = null;
            }
        }

        void CloseTable()
        {
            if (table is not null)
            {
                RenderTable(html, table, tableLine, Fail);
                table = null;
            }
        }

        void AddToParagraph(string text, int number)
        {
            if (paragraph.Count == 0)
            {
                paragraphLine = number;
            }

            paragraph.Add(text);
        }

        void CloseAll()
        {
            CloseParagraph();
            CloseList();
            CloseTable();
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var number = index + 1;
            var line = lines[index].Trim();

            if (line.Length == 0)
            {
                CloseAll();
                continue;
            }

            if (line.StartsWith('|'))
            {
                CloseParagraph();
                CloseList();
                if (table is null)
                {
                    table = [];
                    tableLine = number;
                }

                table.Add(Cells(line));
                continue;
            }

            CloseTable();

            if (line.StartsWith('#'))
            {
                CloseAll();
                var level = line.TakeWhile(c => c == '#').Count();
                var text = line[level..].Trim();
                if (level is not (2 or 3) || line.Length == level || line[level] != ' ' || text.Length == 0)
                {
                    Fail(number, "titre attendu sous la forme « ## Titre » ou « ### Sous-titre ».");
                    AddToParagraph(line, number);
                    continue;
                }

                html.Append("<h").Append(level).Append('>').Append(Inline(text, number, Fail)).Append("</h").Append(level).Append(">\n");
                continue;
            }

            if (HorizontalRule().IsMatch(line))
            {
                CloseAll();
                html.Append("<hr>\n");
                continue;
            }

            var item = ListItem().Match(line);
            if (item.Success)
            {
                CloseParagraph();
                var kind = item.Groups["ordered"].Success ? "ol" : "ul";
                if (list != kind)
                {
                    CloseList();
                    html.Append('<').Append(kind).Append(">\n");
                    list = kind;
                }

                html.Append("<li>").Append(Inline(item.Groups["text"].Value, number, Fail)).Append("</li>\n");
                continue;
            }

            CloseList();
            AddToParagraph(line, number);
        }

        CloseAll();
        return (html.ToString(), error);
    }

    private static void RenderTable(StringBuilder html, List<string[]> rows, int firstLine, Action<int, string> fail)
    {
        if (rows.Count < 2 || !rows[1].All(cell => TableSeparator().IsMatch(cell)))
        {
            fail(firstLine + 1, "un tableau commence par une ligne d'en-tête suivie de « | --- | --- | ».");
            foreach (var row in rows)
            {
                html.Append("<p>").Append(Encoder.Encode("| " + string.Join(" | ", row) + " |")).Append("</p>\n");
            }

            return;
        }

        var columns = rows[0].Length;
        html.Append("<div class=\"table-wrap\"><table>\n<thead><tr>");
        foreach (var cell in rows[0])
        {
            html.Append("<th scope=\"col\">").Append(Inline(cell, firstLine, fail)).Append("</th>");
        }

        html.Append("</tr></thead>\n<tbody>\n");
        for (var index = 2; index < rows.Count; index++)
        {
            var line = firstLine + index;
            if (rows[index].Length != columns)
            {
                fail(line, $"{columns.ToString(CultureInfo.InvariantCulture)} colonnes attendues, comme l'en-tête du tableau.");
            }

            html.Append("<tr>");
            for (var column = 0; column < columns; column++)
            {
                var cell = column < rows[index].Length ? rows[index][column] : string.Empty;
                html.Append("<td>").Append(Inline(cell, line, fail)).Append("</td>");
            }

            html.Append("</tr>\n");
        }

        html.Append("</tbody>\n</table></div>\n");
    }

    private static string[] Cells(string line)
    {
        var content = line.Trim();
        content = content[1..];
        if (content.EndsWith('|'))
        {
            content = content[..^1];
        }

        return content.Split('|').Select(cell => cell.Trim()).ToArray();
    }

    /// <summary>Texte d'une ligne : échappé, puis gras et liens autorisés.</summary>
    private static string Inline(string text, int line, Action<int, string> fail)
    {
        const string LinkRefused = "lien refusé : seuls [texte](https://…) et [texte](mailto:…) sont acceptés, sans espace ni parenthèse dans l'adresse.";
        var html = new StringBuilder();
        var position = 0;

        // Un lien mal formé reste du texte ; il est signalé pour que l'auteur ne le croie pas actif.
        string Text(string segment)
        {
            if (segment.Contains("](", StringComparison.Ordinal))
            {
                fail(line, LinkRefused);
            }

            return Bold(segment);
        }

        foreach (Match link in Link().Matches(text))
        {
            html.Append(Text(text[position..link.Index]));
            var label = link.Groups["label"].Value;
            var target = link.Groups["target"].Value;
            if (IsAllowedLink(target))
            {
                html.Append("<a href=\"").Append(Encoder.Encode(target)).Append('"')
                    .Append(target.StartsWith("https:", StringComparison.Ordinal) ? " rel=\"noopener noreferrer\"" : string.Empty)
                    .Append('>').Append(Bold(label)).Append("</a>");
            }
            else
            {
                fail(line, LinkRefused);
                html.Append(Bold(link.Value));
            }

            position = link.Index + link.Length;
        }

        html.Append(Text(text[position..]));
        return html.ToString();
    }

    private static string Bold(string text) => BoldMarker().Replace(Encoder.Encode(text), "<strong>$1</strong>");

    private static bool IsAllowedLink(string target)
    {
        if (target.StartsWith("mailto:", StringComparison.Ordinal))
        {
            return SettingKeys.IsPlainEmail(target["mailto:".Length..]);
        }

        return Uri.TryCreate(target, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.OriginalString == target;
    }

    [GeneratedRegex(@"^(?:-\s*){3,}$")]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"^(?:(?<ordered>\d{1,3}\.)|[-*])\s+(?<text>.+)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^:?-{3,}:?$")]
    private static partial Regex TableSeparator();

    [GeneratedRegex(@"\[(?<label>[^\[\]]+)\]\((?<target>[^()\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldMarker();
}
