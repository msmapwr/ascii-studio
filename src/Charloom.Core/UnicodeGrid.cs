using System.Globalization;
using System.Text;

namespace Charloom.Core;

public readonly record struct GridGlyph(string Text, int Utf16Index, int Column, int Width);

/// <summary>Stable terminal policy: ambiguous characters are narrow, emoji clusters are wide.</summary>
public static class UnicodeGrid
{
    public static IEnumerable<GridGlyph> Glyphs(string line)
    {
        var iterator = StringInfo.GetTextElementEnumerator(line);
        var column = 0;
        while (iterator.MoveNext())
        {
            var text = iterator.GetTextElement();
            var width = text == "\t" ? 4 - column % 4 : GlyphWidth(text);
            yield return new(text, iterator.ElementIndex, column, width);
            column = checked(column + width);
        }
    }

    public static int Width(string line)
    {
        if (line.All(c => c is >= ' ' and <= '~')) return line.Length;
        var width = 0;
        foreach (var glyph in Glyphs(line)) width = checked(glyph.Column + glyph.Width);
        return width;
    }

    public static int GlyphWidth(string text)
    {
        var visible = false;
        var wide = false;
        var emoji = text.Contains('\uFE0F') || text.Contains('\u20E3');
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark or UnicodeCategory.SpacingCombiningMark) continue;
            visible = true;
            wide |= IsWide(rune.Value);
        }
        return visible ? wide || emoji ? 2 : 1 : 0;
    }

    private static bool IsWide(int codePoint)
    {
        var ranges = UnicodeWidthData.Wide;
        var low = 0; var high = ranges.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (codePoint < ranges[middle].Start) high = middle - 1;
            else if (codePoint > ranges[middle].End) low = middle + 1;
            else return true;
        }
        return false;
    }

    public static string ExpandTabs(string text) => string.Join('\n', TextUtilities.Normalize(text).Split('\n').Select(line =>
    {
        if (!line.Contains('\t')) return line;
        var output = new StringBuilder();
        foreach (var glyph in Glyphs(line)) output.Append(glyph.Text == "\t" ? new string(' ', glyph.Width) : glyph.Text);
        return output.ToString();
    }));

    public static string PadRight(string text, int width) => text + new string(' ', Math.Max(0, width - Width(text)));

    public static AsciiDocument Upgrade(AsciiDocument document)
    {
        document.Validate();
        if (document.GridVersion == 1) return document.Text.Contains('\t') ? document with { Text = ExpandTabs(document.Text) } : document;
        var lines = document.Text.Split('\n');
        var width = Math.Max(document.Width, lines.Max(Width));
        if ((long)width * document.Height > ImageResourceLimits.SamplePoints) throw new ArgumentException("升级后的 Unicode 网格超过 4000 万列位限制。");
        uint[]? Migrate(uint[]? source, uint fallback)
        {
            if (source is null) return null;
            var target = Enumerable.Repeat(fallback, width * document.Height).ToArray();
            for (var y = 0; y < document.Height; y++)
            {
                foreach (var glyph in Glyphs(lines[y]))
                    for (var offset = 0; offset < glyph.Width; offset++)
                        target[y * width + glyph.Column + offset] = source[y * document.Width + glyph.Utf16Index];
                var contentWidth = Width(lines[y]);
                for (var x = lines[y].Length; x < document.Width && contentWidth + x - lines[y].Length < width; x++)
                    target[y * width + contentWidth + x - lines[y].Length] = source[y * document.Width + x];
            }
            return target;
        }
        var upgraded = document with
        {
            GridVersion = 1,
            Width = width,
            Text = ExpandTabs(document.Text),
            Colors = Migrate(document.Colors, 0xFFE7EDF7),
            BackgroundColors = Migrate(document.BackgroundColors, 0xFF121822)
        };
        upgraded.Validate();
        return upgraded;
    }
}
