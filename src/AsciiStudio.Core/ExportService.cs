using System.Net;
using System.Text;
using System.Text.Json;

namespace AsciiStudio.Core;

public static class ExportService
{
    public static string Html(AsciiDocument document)
    {
        document.Validate();
        var content = new StringBuilder(); var lines = document.Text.Split('\n');
        for (var y = 0; y < lines.Length; y++)
        {
            if (document.Colors is null && document.BackgroundColors is null) content.Append(WebUtility.HtmlEncode(lines[y]));
            else for (var x = 0; x < lines[y].Length;)
            {
                var start = x; var color = document.Colors?[y * document.Width + x] ?? 0xFFE7EDF7;
                var background = document.BackgroundColors?[y * document.Width + x];
                while (x < lines[y].Length && (document.Colors?[y * document.Width + x] ?? 0xFFE7EDF7) == color && document.BackgroundColors?[y * document.Width + x] == background) x++;
                var bg = background.HasValue ? $";background-color:#{background.Value & 0xFFFFFF:X6}" : "";
                content.Append($"<span style=\"color:#{color & 0xFFFFFF:X6}{bg}\">{WebUtility.HtmlEncode(lines[y][start..x])}</span>");
            }
            if (y < lines.Length - 1) content.Append('\n');
        }
        var family = WebUtility.HtmlEncode("'" + document.FontFamily.Replace("\\", "\\\\").Replace("'", "\\'") + "',monospace");
        var lineHeight = document.CellHeight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return $"<!doctype html><html lang=\"zh\"><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(document.Title)}</title><style>body{{background:#121822;color:#e7edf7;padding:24px}}pre{{font-size:13px;line-height:{lineHeight}px;white-space:pre}}</style><pre style=\"font-family:{family}\">{content}</pre></html>";
    }

    public static string Svg(AsciiDocument document)
    {
        document.Validate(); var cw = document.CellWidth; var ch = document.CellHeight; const int pad = 20;
        string Number(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var result = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Number(document.Width * cw + 2 * pad)}\" height=\"{Number(document.Height * ch + 2 * pad)}\"><rect width=\"100%\" height=\"100%\" fill=\"#121822\"/><g font-family=\"{WebUtility.HtmlEncode(document.FontFamily)},monospace\" font-size=\"13\" fill=\"#e7edf7\" xml:space=\"preserve\">");
        var lines = document.Text.Split('\n');
        if (document.BackgroundColors is not null)
            for (var y = 0; y < lines.Length; y++)
                for (var x = 0; x < document.Width;)
                {
                    var start = x; var background = document.BackgroundColors[y * document.Width + x];
                    while (x < document.Width && document.BackgroundColors[y * document.Width + x] == background) x++;
                    result.Append($"<rect x=\"{Number(pad + start * cw)}\" y=\"{Number(pad + y * ch)}\" width=\"{Number((x - start) * cw)}\" height=\"{Number(ch)}\" fill=\"#{background & 0xFFFFFF:X6}\"/>");
                }
        for (var y = 0; y < lines.Length; y++)
        {
            if (document.Colors is null) result.Append($"<text x=\"{pad}\" y=\"{Number(pad + (y + 1) * ch)}\">{WebUtility.HtmlEncode(lines[y])}</text>");
            else for (var x = 0; x < lines[y].Length; x++) result.Append($"<text x=\"{Number(pad + x * cw)}\" y=\"{Number(pad + (y + 1) * ch)}\" fill=\"#{document.Colors[y * document.Width + x] & 0xFFFFFF:X6}\">{WebUtility.HtmlEncode(lines[y][x].ToString())}</text>");
        }
        return result.Append("</g></svg>").ToString();
    }

    public static string Ansi(AsciiDocument document)
    {
        document.Validate(); if (document.Colors is null && document.BackgroundColors is null) return TextUtilities.Clean(document.Text, false).Replace("\x1b", "");
        var result = new StringBuilder(); uint previous = 0; uint? previousBackground = null; var lines = document.Text.Split('\n');
        for (var y = 0; y < lines.Length; y++)
        {
            for (var x = 0; x < lines[y].Length; x++)
            {
                var c = document.Colors?[y * document.Width + x] ?? 0xFFE7EDF7;
                if (c != previous) { result.Append($"\x1b[38;2;{c >> 16 & 255};{c >> 8 & 255};{c & 255}m"); previous = c; }
                var background = document.BackgroundColors?[y * document.Width + x];
                if (background.HasValue && background != previousBackground) { var b = background.Value; result.Append($"\x1b[48;2;{b >> 16 & 255};{b >> 8 & 255};{b & 255}m"); previousBackground = background; }
                if (!char.IsControl(lines[y][x])) result.Append(lines[y][x]);
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
}
