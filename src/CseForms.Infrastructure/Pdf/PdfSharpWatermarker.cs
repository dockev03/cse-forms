using System.Globalization;
using System.Text;
using CseForms.Application.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace CseForms.Infrastructure.Pdf;

/// <summary>
/// Filigrane en diagonale sur chaque page, semi-transparent, au-dessus du contenu. Écrit directement en
/// opérateurs PDF avec la police standard Helvetica (fournie par tout lecteur, rien à embarquer ni à
/// résoudre sur le serveur) : PDFsharp ne sert qu'à lire et réécrire le document.
/// </summary>
internal sealed class PdfSharpWatermarker : IPdfWatermarker
{
    private const string FontName = "/CseWatermarkFont";
    private const string StateName = "/CseWatermarkState";
    private const double TitleMaxSize = 64;
    private const double DetailMaxSize = 22;

    public byte[] Apply(byte[] pdf, PdfWatermarkText text)
    {
        using var input = new MemoryStream(pdf, writable: false);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        var font = new PdfDictionary(document);
        font.Elements["/Type"] = new PdfName("/Font");
        font.Elements["/Subtype"] = new PdfName("/Type1");
        font.Elements["/BaseFont"] = new PdfName("/Helvetica-Bold");
        font.Elements["/Encoding"] = new PdfName("/WinAnsiEncoding");
        document.Internals.AddObject(font);

        var state = new PdfDictionary(document);
        state.Elements["/Type"] = new PdfName("/ExtGState");
        state.Elements["/ca"] = new PdfReal(0.18);
        state.Elements["/CA"] = new PdfReal(0.18);
        document.Internals.AddObject(state);

        foreach (var page in document.Pages)
        {
            Register(page.Resources, "/Font", FontName, font);
            Register(page.Resources, "/ExtGState", StateName, state);

            // Le contenu existant est isolé (q ... Q) : son état graphique ne déforme pas le filigrane.
            page.Contents.PrependContent().CreateStream("q\n"u8.ToArray());
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("Q\n").Concat(Overlay(page, text)).ToArray());
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static void Register(PdfResources resources, string category, string name, PdfDictionary value)
    {
        if (resources.Elements.GetDictionary(category) is not { } dictionary)
        {
            dictionary = new PdfDictionary();
            resources.Elements[category] = dictionary;
        }

        dictionary.Elements[name] = value.Reference;
    }

    private static byte[] Overlay(PdfPage page, PdfWatermarkText text)
    {
        var box = page.CropBox.IsEmpty ? page.MediaBox : page.CropBox;
        var (width, height) = (box.X2 - box.X1, box.Y2 - box.Y1);
        var (centerX, centerY) = (box.X1 + (width / 2), box.Y1 + (height / 2));
        var angle = Math.Atan2(height, width);
        var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
        var usable = Math.Sqrt((width * width) + (height * height)) * 0.8;

        var title = WinAnsi(text.Title.ToUpperInvariant());
        var detail = WinAnsi(text.Detail);
        var titleSize = Math.Min(TitleMaxSize, usable / HelveticaBold.Width(title));
        var detailSize = Math.Min(DetailMaxSize, usable / HelveticaBold.Width(detail));

        using var content = new MemoryStream();
        Write(content, $"q\n{StateName} gs\n0.55 0 0 rg\n");
        WriteLine(content, title, titleSize, offset: detailSize * 0.6);
        WriteLine(content, detail, detailSize, offset: -detailSize * 1.4);
        Write(content, "Q\n");
        return content.ToArray();

        void WriteLine(Stream stream, byte[] line, double size, double offset)
        {
            var half = HelveticaBold.Width(line) * size / 2;
            var x = centerX - (half * cos) - (offset * sin);
            var y = centerY - (half * sin) + (offset * cos);
            Write(stream, $"BT\n{FontName} {N(size)} Tf\n{N(cos)} {N(sin)} {N(-sin)} {N(cos)} {N(x)} {N(y)} Tm\n(");
            foreach (var b in line)
            {
                // Chaîne littérale PDF : parenthèses et barre oblique inverse échappées.
                if (b is (byte)'(' or (byte)')' or (byte)'\\')
                {
                    stream.WriteByte((byte)'\\');
                }

                stream.WriteByte(b);
            }
            Write(stream, ") Tj\nET\n");
        }
    }

    private static void Write(Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Texte en WinAnsi. Un caractère hors de cet encodage est remplacé
    /// par sa lettre de base (ą en a, ł en l), sinon par ?, un caractère de contrôle est retiré.
    /// </summary>
    internal static byte[] WinAnsi(string value)
    {
        var bytes = new List<byte>(value.Length);
        foreach (var c in value)
        {
            var code = Encode(c) ?? Encode(BaseLetter(c)) ?? (byte)'?';
            if (code >= 0x20)
            {
                bytes.Add(code);
            }
        }

        return [.. bytes];
    }

    private static byte? Encode(char c) => c switch
    {
        >= ' ' and <= '~' => (byte)c,
        >= ' ' and <= 'ÿ' => (byte)c,
        < ' ' => 0,
        _ => SpecialWinAnsi.TryGetValue(c, out var code) ? code : null,
    };

    private static char BaseLetter(char c)
    {
        if (c switch { 'Ł' => 'L', 'ł' => 'l', 'Đ' => 'D', 'đ' => 'd', 'Ħ' => 'H', 'ħ' => 'h', 'ı' => 'i', _ => '\0' } is not '\0' and var plain)
        {
            return plain;
        }

        var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        return decomposed.Length > 0 && decomposed[0] != c ? decomposed[0] : '?';
    }

    private static readonly Dictionary<char, byte> SpecialWinAnsi = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87,
        ['ˆ'] = 0x88, ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E, ['‘'] = 0x91,
        ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['˜'] = 0x98,
        ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B, ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
    };

    /// <summary>Chasses de Helvetica-Bold (métriques Adobe, en millièmes de corps) pour centrer le texte.</summary>
    private static class HelveticaBold
    {
        // Caractères 32 à 126.
        private static readonly int[] Ascii =
        [
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
            333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
        ];

        /// <summary>Largeur en unités de corps (1 = la taille de la police) ; les lettres accentuées comptent comme leur base.</summary>
        public static double Width(byte[] encoded)
        {
            var total = 0;
            foreach (var b in encoded)
            {
                total += b is >= 32 and <= 126 ? Ascii[b - 32] : 611;
            }

            return Math.Max(total, 1) / 1000.0;
        }
    }
}
