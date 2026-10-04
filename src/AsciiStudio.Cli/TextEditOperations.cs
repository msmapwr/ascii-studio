using System.Globalization;
using AsciiStudio.Core;

namespace AsciiStudio.Cli;

public static class TextEditOperations
{
    public static int Offset(string line, int column)
    {
        if (column < 0) throw new CliUsageException("Columns cannot be negative.");
        foreach (var glyph in UnicodeGrid.Glyphs(line))
        {
            if (glyph.Column == column) return glyph.Utf16Index;
            if (glyph.Column < column && glyph.Column + glyph.Width > column) throw new CliUsageException("Selection cuts a wide glyph or grapheme cluster.");
        }
        if (column == UnicodeGrid.Width(line)) return line.Length;
        throw new CliUsageException("Column is outside the line.");
    }
    public static void ValidateSelection(string text, EditSelection selection)
    {
        var lines = text.Split('\n');
        if (selection.Row < 0 || selection.EndRow < selection.Row || selection.EndRow >= lines.Length || selection.Column < 0 || selection.EndColumn < 0
            || (selection.Rectangle || selection.Row == selection.EndRow) && selection.EndColumn < selection.Column)
            throw new CliUsageException("Invalid selection; zero-based rows/columns and exclusive end column required.");
        if (selection.Rectangle)
            for (var row = selection.Row; row <= selection.EndRow; row++) { _ = Offset(lines[row], selection.Column); _ = Offset(lines[row], selection.EndColumn); }
        else { _ = Offset(lines[selection.Row], selection.Column); _ = Offset(lines[selection.EndRow], selection.EndColumn); }
    }
    public static string Selected(string text, EditSelection selection)
    {
        ValidateSelection(text, selection); var lines = text.Split('\n');
        if (selection.Rectangle) return string.Join('\n', Enumerable.Range(selection.Row, selection.EndRow - selection.Row + 1)
            .Select(row => lines[row][Offset(lines[row], selection.Column)..Offset(lines[row], selection.EndColumn)]));
        var start = Position(lines, selection.Row, selection.Column); var end = Position(lines, selection.EndRow, selection.EndColumn);
        return text[start..end];
    }
    public static string Replace(string text, EditSelection selection, string replacement)
    {
        ValidateSelection(text, selection); replacement = UnicodeGrid.ExpandTabs(replacement); var lines = text.Split('\n');
        if (!selection.Rectangle)
        { var start = Position(lines, selection.Row, selection.Column); var end = Position(lines, selection.EndRow, selection.EndColumn); return text[..start] + replacement + text[end..]; }
        var replacements = replacement.Split('\n'); var height = selection.EndRow - selection.Row + 1;
        if (replacements.Length != 1 && replacements.Length != height) throw new CliUsageException("Rectangle replacement needs one line or exactly the selected row count.");
        for (var row = selection.Row; row <= selection.EndRow; row++)
            lines[row] = lines[row][..Offset(lines[row], selection.Column)] + replacements[replacements.Length == 1 ? 0 : row - selection.Row]
                + lines[row][Offset(lines[row], selection.EndColumn)..];
        return string.Join('\n', lines);
    }
    private static int Position(string[] lines, int row, int column) => lines.Take(row).Sum(line => line.Length + 1) + Offset(lines[row], column);
    public static string Transform(string text, string operation) => operation switch
    {
        "upper" => text.ToUpperInvariant(), "lower" => text.ToLowerInvariant(), "mirror" => string.Join('\n', text.Split('\n').Select(line => string.Concat(TextArtLayout.Elements(line).Reverse()))),
        "flip" => string.Join('\n', text.Split('\n').Reverse()), "trim" => TextUtilities.TrimCanvas(text), "clean" => TextUtilities.Clean(text, false),
        "ascii" => TextUtilities.Clean(text, true), "expand-tabs" => UnicodeGrid.ExpandTabs(text), _ => throw new CliUsageException("Unsupported transform; upper/lower/mirror/flip/trim/clean/ascii/expand-tabs.")
    };
    public static (int Row, int Column, int EndRow, int EndColumn)[] Find(string text, string needle, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(needle)) throw new CliUsageException("Search text cannot be empty.");
        needle = TextUtilities.Normalize(needle); var index = new DocumentViewIndex(AsciiDocument.FromText(text));
        var matches = new List<(int, int, int, int)>(); var offset = 0;
        while (offset <= text.Length - needle.Length)
        {
            var found = text.IndexOf(needle, offset, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); if (found < 0) break;
            var end = found + needle.Length;
            if (Boundary(text, found) && Boundary(text, end))
            {
                var a = index.Position(found); var b = index.Position(end); matches.Add((a.Row, a.Column, b.Row, b.Column));
                if (matches.Count > 1000) throw new CliUsageException("Search exceeds 1000 matches; narrow the search.");
            }
            offset = end;
        }
        return matches.ToArray();
    }
    private static bool Boundary(string text, int offset)
    {
        if (offset == text.Length) return true;
        var row = text.LastIndexOf('\n', Math.Max(0, offset - 1), offset == 0 ? 0 : offset) + 1;
        for (var index = row; index <= offset; index += StringInfo.GetNextTextElementLength(text, index))
            if (index == offset) return true;
        return false;
    }
}
