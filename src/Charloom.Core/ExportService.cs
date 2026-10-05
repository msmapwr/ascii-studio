using System.Net;
using System.Text;
using System.Text.Json;

namespace Charloom.Core;

public static class ExportService
{
    public const int MaximumMarkupCharacters = 16_000_000;
    private static void AppendMarkup(StringBuilder output, string markup)
    {
        if ((long)output.Length + markup.Length > MaximumMarkupCharacters) throw new ArgumentException("HTML / SVG 标记超过 1600 万字符上限，请降低网格尺寸。");
        output.Append(markup);
    }
    public static string Html(AsciiDocument document)
    {
        document = UnicodeGrid.Upgrade(document);
        var content = new StringBuilder(); var lines = document.Text.Split('\n');
        for (var y = 0; y < lines.Length; y++)
        {
            foreach (var glyph in UnicodeGrid.Glyphs(lines[y]))
            {
                if (glyph.Width == 0) continue;
                var color = document.Colors?[y * document.Width + glyph.Column] ?? 0xFFE7EDF7;
                var background = document.BackgroundColors?[y * document.Width + glyph.Column];
                var bg = background.HasValue ? $";background-color:{CssColor(background.Value)}" : "";
                var width = (glyph.Width * document.CellWidth).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                var foreground = $"color:{CssColor(color)}";
                if (ImageQualityConverter.BlockFill(glyph.Text) is { } block)
                {
                    var top = (block.Top * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                    var end = ((block.Top + block.Height) * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                    var baseColor = background.HasValue ? CssColor(background.Value) : "transparent";
                    foreground = $"color:transparent;background-image:linear-gradient(to bottom,{baseColor} 0%,{baseColor} {top}%,{CssColor(color)} {top}%,{CssColor(color)} {end}%,{baseColor} {end}%)";
                    bg = "";
                }
                AppendMarkup(content, $"<span style=\"display:inline-block;vertical-align:top;width:{width}px;{foreground}{bg}\">{WebUtility.HtmlEncode(glyph.Text)}</span>");
            }
            if (y < lines.Length - 1) content.Append('\n');
        }
        var family = WebUtility.HtmlEncode("'" + document.FontFamily.Replace("\\", "\\\\").Replace("'", "\\'") + "','Microsoft YaHei UI','Segoe UI Emoji',monospace");
        var lineHeight = document.CellHeight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return $"<!doctype html><html lang=\"zh\"><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(document.Title)}</title><style>body{{background:#121822;color:#e7edf7;padding:24px}}pre{{font-size:13px;line-height:{lineHeight}px;white-space:pre}}</style><pre style=\"font-family:{family}\">{content}</pre></html>";
    }

    public static string Svg(AsciiDocument document)
    {
        document = UnicodeGrid.Upgrade(document); System.Xml.XmlConvert.VerifyXmlChars(document.Text); System.Xml.XmlConvert.VerifyXmlChars(document.FontFamily);
        var cw = document.CellWidth; var ch = document.CellHeight; const int pad = 20;
        string Number(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var result = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Number(document.Width * cw + 2 * pad)}\" height=\"{Number(document.Height * ch + 2 * pad)}\"><rect width=\"100%\" height=\"100%\" fill=\"#121822\"/><g font-family=\"{WebUtility.HtmlEncode(document.FontFamily)},Microsoft YaHei UI,Segoe UI Emoji,monospace\" font-size=\"13\" fill=\"#e7edf7\" xml:space=\"preserve\">");
        var lines = document.Text.Split('\n');
        if (document.BackgroundColors is not null)
            for (var y = 0; y < lines.Length; y++)
                for (var x = 0; x < document.Width;)
                {
                    var start = x; var background = document.BackgroundColors[y * document.Width + x];
                    while (x < document.Width && document.BackgroundColors[y * document.Width + x] == background) x++;
                    AppendMarkup(result, $"<rect x=\"{Number(pad + start * cw)}\" y=\"{Number(pad + y * ch)}\" width=\"{Number((x - start) * cw)}\" height=\"{Number(ch)}\" fill=\"#{background & 0xFFFFFF:X6}\"{SvgAlpha(background)}/>");
                }
        for (var y = 0; y < lines.Length; y++)
        {
            if (document.Colors is null && lines[y].All(char.IsAscii))
                AppendMarkup(result, $"<text x=\"{pad}\" y=\"{Number(pad + (y + 1) * ch)}\" textLength=\"{Number(lines[y].Length * cw)}\" lengthAdjust=\"spacingAndGlyphs\">{WebUtility.HtmlEncode(lines[y])}</text>");
            else foreach (var glyph in UnicodeGrid.Glyphs(lines[y]))
            {
                if (glyph.Width == 0) continue;
                var color = document.Colors?[y * document.Width + glyph.Column] ?? 0xFFE7EDF7;
                if (ImageQualityConverter.BlockFill(glyph.Text) is { } block)
                {
                    if (color >> 24 < 255)
                        AppendMarkup(result, $"<rect x=\"{Number(pad + glyph.Column * cw)}\" y=\"{Number(pad + (y + block.Top) * ch)}\" width=\"{Number(cw)}\" height=\"{Number(block.Height * ch)}\" fill=\"#121822\"/>");
                    AppendMarkup(result, $"<rect x=\"{Number(pad + glyph.Column * cw)}\" y=\"{Number(pad + (y + block.Top) * ch)}\" width=\"{Number(cw)}\" height=\"{Number(block.Height * ch)}\" fill=\"#{color & 0xFFFFFF:X6}\"{SvgAlpha(color)}/>");
                    continue;
                }
                AppendMarkup(result, $"<text x=\"{Number(pad + glyph.Column * cw)}\" y=\"{Number(pad + (y + 1) * ch)}\" textLength=\"{Number(glyph.Width * cw)}\" lengthAdjust=\"spacingAndGlyphs\" fill=\"#{color & 0xFFFFFF:X6}\"{SvgAlpha(color)}>{WebUtility.HtmlEncode(glyph.Text)}</text>");
            }
        }
        return result.Append("</g></svg>").ToString();
    }

    public static string Ansi(AsciiDocument document)
    {
        document = UnicodeGrid.Upgrade(document); if (document.Colors is null && document.BackgroundColors is null) return TextUtilities.Clean(document.Text, false).Replace("\x1b", "");
        var result = new StringBuilder(); uint previous = 0; uint? previousBackground = null; var lines = document.Text.Split('\n');
        for (var y = 0; y < lines.Length; y++)
        {
            foreach (var glyph in UnicodeGrid.Glyphs(lines[y]))
            {
                var x = glyph.Column;
                if (glyph.Width == 0) continue;
                var c = document.Colors?[y * document.Width + x] ?? 0xFFE7EDF7;
                if (c != previous) { result.Append(AnsiColor(c, false, document.ColorEncoding)); previous = c; }
                var background = document.BackgroundColors?[y * document.Width + x];
                if (background.HasValue && background != previousBackground) { result.Append(AnsiColor(background.Value, true, document.ColorEncoding)); previousBackground = background; }
                result.Append(glyph.Text);
            }
            if (y < lines.Length - 1) result.Append("\r\n");
        }
        return result.Append("\x1b[0m").ToString();
    }

    public static string Markdown(AsciiDocument document)
    {
        document.Validate(); var longest = 0; var run = 0;
        foreach (var c in document.Text) { run = c == '`' ? run + 1 : 0; longest = Math.Max(longest, run); }
        var fence = new string('`', Math.Max(3, longest + 1)); return fence + "text\n" + document.Text + "\n" + fence + "\n";
    }
    public static string Json(AsciiDocument document) { document.Validate(); return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }); }
    private static string AnsiColor(uint c, bool background, AnsiColorEncoding encoding)
    {
        if (c >> 24 == 0) return background ? "\x1b[49m" : "\x1b[39m";
        if (encoding == AnsiColorEncoding.Ansi16)
        {
            var index = ImagePalettes.Nearest(c, ImagePalettes.Ansi16);
            return $"\x1b[{(index < 8 ? 30 : 90) + index % 8 + (background ? 10 : 0)}m";
        }
        if (encoding == AnsiColorEncoding.Ansi256) return $"\x1b[{(background ? 48 : 38)};5;{ImagePalettes.Nearest(c, ImagePalettes.Ansi256)}m";
        return $"\x1b[{(background ? 48 : 38)};2;{c >> 16 & 255};{c >> 8 & 255};{c & 255}m";
    }
    private static string CssColor(uint c) => c >> 24 == 255 ? $"#{c & 0xFFFFFF:X6}" : $"#{c & 0xFFFFFF:X6}{c >> 24:X2}";
    private static string SvgAlpha(uint c) => c >> 24 == 255 ? "" : $" fill-opacity=\"{((c >> 24) / 255d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}\"";
}
