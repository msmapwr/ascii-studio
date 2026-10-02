using System.Globalization;
using System.Text;

namespace AsciiStudio.Core;

public enum ArtAlignment { Left, Center, Right }
public enum ArtPacking { Default, Full, Kern, Smush }
public sealed record TextArtOptions
{
    public int LetterSpacing { get; init; }
    public int LineSpacing { get; init; }
    public int MaximumWidth { get; init; }
    public bool Wrap { get; init; }
    public ArtAlignment Alignment { get; init; }
    public ArtPacking Horizontal { get; init; }
    public ArtPacking Vertical { get; init; }
    public int Border { get; init; }
    public int PaddingX { get; init; } = 1;
    public int PaddingY { get; init; }
    public string Title { get; init; } = "";
    public bool Trim { get; init; } = true;
    public string Replacement { get; init; } = "";
    public void Validate()
    {
        if (LetterSpacing is < 0 or > 20 || LineSpacing is < 0 or > 20 || MaximumWidth is < 0 or > 2000
            || Border is < 0 or > 3 || PaddingX is < 0 or > 30 || PaddingY is < 0 or > 30
            || !Enum.IsDefined(Alignment) || !Enum.IsDefined(Horizontal) || !Enum.IsDefined(Vertical)
            || Title is null || Title.Length > 100 || Title.Any(char.IsControl)
            || Replacement is null || Replacement.Length > 1 || Replacement.Any(c => char.IsControl(c) || char.IsSurrogate(c) || UnicodeGrid.GlyphWidth(c.ToString()) != 1))
            throw new ArgumentException("文字排版参数无效。");
        if (Wrap && MaximumWidth == 0) throw new ArgumentException("自动换行需要设置最大宽度。");
    }
}

public static class TextArtLayout
{
    public static string Render(string text, Func<string, string> renderLine, TextArtOptions options, int defaultVertical = 0, CancellationToken token = default)
    {
        options.Validate();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new ArgumentException("请输入1到2000个字符。");
        var blocks = new List<string[]>();
        foreach (var source in TextUtilities.Normalize(text).Split('\n'))
        {
            token.ThrowIfCancellationRequested();
            string Draw(string part)
            {
                token.ThrowIfCancellationRequested();
                var art = TextUtilities.Normalize(renderLine(part)).TrimEnd('\n');
                if (art.Length > 4_000_000) throw new ArgumentException("文字画布过大。");
                return options.Trim ? TextUtilities.TrimCanvas(art) : art;
            }
            if (!options.Wrap) { blocks.Add(Draw(source).Split('\n')); continue; }
            var current = "";
            foreach (var element in Elements(source))
            {
                if (Width(Draw(current + element)) <= options.MaximumWidth) { current += element; continue; }
                if (current.Length == 0) throw new ArgumentException("单个字形超过最大宽度，请增大宽度。");
                var breakAt = current.LastIndexOf(' ');
                if (breakAt > 0)
                {
                    blocks.Add(Draw(current[..breakAt]).Split('\n'));
                    current = current[(breakAt + 1)..] + element;
                    if (Width(Draw(current)) <= options.MaximumWidth) continue;
                    current = current[..^element.Length];
                }
                blocks.Add(Draw(current.TrimEnd()).Split('\n')); current = element == " " ? "" : element;
                if (Width(Draw(current)) > options.MaximumWidth) throw new ArgumentException("单个字形超过最大宽度，请增大宽度。");
            }
            blocks.Add(Draw(current).Split('\n'));
        }
        var width = blocks.SelectMany(b => b).Max(UnicodeGrid.Width);
        if (options.MaximumWidth > 0 && width > options.MaximumWidth) throw new ArgumentException($"结果宽度{width}超过最大宽度{options.MaximumWidth}，请开启自动换行或增大宽度。");
        var target = options.MaximumWidth > 0 ? options.MaximumWidth : width;
        var rows = new List<string>();
        var flags = options.Vertical switch { ArtPacking.Full => 0, ArtPacking.Kern => 8192, ArtPacking.Smush => 16384 | 31 << 8, _ => defaultVertical };
        if ((flags & (8192 | 16384)) == 0) flags = 0;
        foreach (var block in blocks)
        {
            token.ThrowIfCancellationRequested();
            var aligned = block.Select(row =>
            {
                var delta = target - UnicodeGrid.Width(row);
                var left = options.Alignment switch { ArtAlignment.Center => delta / 2, ArtAlignment.Right => delta, _ => 0 };
                return UnicodeGrid.PadRight(new string(' ', left) + row, target);
            }).ToArray();
            var overlap = 0;
            if (options.LineSpacing == 0 && flags != 0)
                for (var n = 1; n <= Math.Min(rows.Count, aligned.Length); n++)
                {
                    var valid = true;
                    for (var y = 0; y < n && valid; y++)
                        valid = MergeRow(rows[rows.Count - n + y], aligned[y], flags) is not null;
                    if (!valid) break;
                    overlap = n;
                }
            for (var y = 0; y < overlap; y++) rows[rows.Count - overlap + y] = MergeRow(rows[rows.Count - overlap + y], aligned[y], flags)!;
            if (rows.Count > 0 && overlap == 0) for (var n = 0; n < options.LineSpacing; n++) rows.Add(new string(' ', target));
            rows.AddRange(aligned.Skip(overlap));
            if ((long)rows.Count * target > 4_000_000) throw new ArgumentException("文字画布超过400万单元。");
        }
        var output = string.Join('\n', rows);
        if (options.Replacement.Length == 1) output = output.Replace(' ', options.Replacement[0]);
        return options.Border > 0 ? Frame(output, options) : output;
    }
    public static IEnumerable<string> Elements(string text)
    {
        var iterator = StringInfo.GetTextElementEnumerator(text);
        while (iterator.MoveNext()) yield return iterator.GetTextElement();
    }
    private static int Width(string text) => text.Split('\n').Max(UnicodeGrid.Width);
    private static string? MergeRow(string top, string bottom, int flags)
    {
        // FIGlet artwork is single-column ASCII. Wide raster styles cannot overlap safely.
        if (!top.All(char.IsAscii) || !bottom.All(char.IsAscii)) return null;
        var result = new StringBuilder(top.Length);
        for (var x = 0; x < top.Length; x++)
        {
            var a = top[x]; var b = bottom[x]; char? c = a == ' ' ? b : b == ' ' ? a : null;
            if (c is null && (flags & 16384) != 0)
            {
                if ((flags & 7936) == 0) c = b;
                if ((flags & 256) != 0 && a == b && a != '|') c = a;
                if ((flags & 512) != 0) { if (a == '_' && "|/\\[]{}()<>".Contains(b)) c = b; if (b == '_' && "|/\\[]{}()<>".Contains(a)) c = a; }
                if ((flags & 1024) != 0)
                {
                    string[] hierarchy = ["|", "/\\", "[]", "{}", "()", "<>"];
                    var ai = Array.FindIndex(hierarchy, h => h.Contains(a)); var bi = Array.FindIndex(hierarchy, h => h.Contains(b));
                    if (ai >= 0 && bi >= 0 && ai != bi) c = ai > bi ? a : b;
                }
                if ((flags & 2048) != 0 && (a == '-' && b == '_' || a == '_' && b == '-')) c = '=';
                if ((flags & 4096) != 0 && a == '|' && b == '|') c = '|';
            }
            if (c is null) return null; result.Append(c.Value);
        }
        return result.ToString();
    }
    public static string Frame(string text, TextArtOptions options)
    {
        options.Validate(); var rows = text.Split('\n'); var width = rows.Max(UnicodeGrid.Width);
        var inner = Math.Max(width + options.PaddingX * 2, options.Title.Length == 0 ? 0 : UnicodeGrid.Width(options.Title) + 2);
        if (inner + 2 > 2000 || (long)(rows.Length + options.PaddingY * 2 + 2) * (inner + 2) > 4_000_000) throw new ArgumentException("边框画布过大。");
        var (tl, tr, bl, br, h, v) = options.Border switch { 2 => ('╔', '╗', '╚', '╝', '═', '║'), 3 => ('*', '*', '*', '*', '*', '*'), _ => ('+', '+', '+', '+', '-', '|') };
        var title = options.Title.Length == 0 ? "" : " " + options.Title + " ";
        var result = new List<string> { tl + title + new string(h, inner - UnicodeGrid.Width(title)) + tr };
        for (var i = 0; i < options.PaddingY; i++) result.Add(v + new string(' ', inner) + v);
        result.AddRange(rows.Select(r => v + new string(' ', options.PaddingX) + UnicodeGrid.PadRight(r, inner - options.PaddingX * 2) + new string(' ', options.PaddingX) + v));
        for (var i = 0; i < options.PaddingY; i++) result.Add(v + new string(' ', inner) + v);
        result.Add(bl + new string(h, inner) + br); return string.Join('\n', result);
    }
}
