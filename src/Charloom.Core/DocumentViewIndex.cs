using System.Globalization;

namespace Charloom.Core;

public readonly record struct TextPageRange(int Start, int Length, int FirstRow);

/// <summary>Reusable row and bounded editor page index. Page boundaries preserve grapheme clusters.</summary>
public sealed class DocumentViewIndex
{
    public AsciiDocument Document { get; }
    public int[] RowStarts { get; }
    public TextPageRange[] Pages { get; }
    public bool IsPaged => Document.Text.Length > 200_000;
    public DocumentViewIndex(AsciiDocument document, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        Document = UnicodeGrid.Upgrade(document);
        var text = Document.Text;
        var rows = new List<int> { 0 };
        var pages = new List<TextPageRange>();
        var start = 0; var firstRow = 0; var row = 0; var nextCancellationCheck = 0;
        for (var offset = 0; offset < text.Length;)
        {
            if (offset >= nextCancellationCheck) { cancellation.ThrowIfCancellationRequested(); nextCancellationCheck = offset + 1024; }
            if (offset > start && (offset - start >= 64_000 || row - firstRow >= 200))
            { pages.Add(new(start, offset - start, firstRow)); start = offset; firstRow = row; }
            var length = StringInfo.GetNextTextElementLength(text, offset);
            if (text[offset] == '\n') { rows.Add(offset + 1); row++; }
            offset += length;
        }
        pages.Add(new(start, text.Length - start, firstRow));
        RowStarts = Document.Height == 0 ? [] : rows.ToArray();
        Pages = IsPaged ? pages.ToArray() : [new(0, text.Length, 0)];
    }

    public static string Prefix(string text, int maximumLength)
    {
        if (maximumLength < 0) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        var end = 0;
        while (end < text.Length)
        {
            var length = StringInfo.GetNextTextElementLength(text, end);
            if (end + length > maximumLength) break;
            end += length;
        }
        return text[..end];
    }

    public string Line(int row)
    {
        if (row < 0 || row >= RowStarts.Length) return "";
        var start = RowStarts[row];
        var end = row + 1 < RowStarts.Length ? RowStarts[row + 1] - 1 : Document.Text.Length;
        return Document.Text[start..end];
    }
    public int PageAt(int offset)
    {
        offset = Math.Clamp(offset, 0, Document.Text.Length);
        var low = 0; var high = Pages.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Pages[middle].Start <= offset) low = middle; else high = middle - 1;
        }
        return low;
    }
    public (int Row, int Column) Position(int offset)
    {
        if (RowStarts.Length == 0) return (0, 0);
        offset = Math.Clamp(offset, 0, Document.Text.Length);
        var row = Array.BinarySearch(RowStarts, offset);
        if (row < 0) row = ~row - 1;
        return (row, UnicodeGrid.Width(Document.Text[RowStarts[row]..offset]));
    }
    public string ReplacePage(int page, string text)
    {
        if (page < 0 || page >= Pages.Length) throw new ArgumentOutOfRangeException(nameof(page));
        var range = Pages[page];
        return Document.Text[..range.Start] + TextUtilities.Normalize(text) + Document.Text[(range.Start + range.Length)..];
    }
}

public readonly record struct ViewportRegion(double X, double Y, double Width, double Height, double Density)
{
    public int PixelWidth => Math.Max(1, (int)Math.Ceiling(Width * Density));
    public int PixelHeight => Math.Max(1, (int)Math.Ceiling(Height * Density));
    public static ViewportRegion Create(double x, double y, double width, double height, double density)
    {
        if (!new[] { x, y, width, height, density }.All(double.IsFinite) || width <= 0 || height <= 0 || density <= 0)
            throw new ArgumentException("可见区域参数无效。");
        width = Math.Min(width + 32, 8192); height = Math.Min(height + 32, 8192);
        density = Math.Min(density, Math.Min(4096 / width, 4096 / height));
        density = Math.Min(density, Math.Sqrt(4_000_000 / (width * height)));
        // Ceiling each dimension must still fit the allocation budget.
        while ((long)Math.Ceiling(width * density) * (long)Math.Ceiling(height * density) > 4_000_000) density *= .999;
        return new(Math.Max(0, x - 16), Math.Max(0, y - 16), width, height, density);
    }
}
