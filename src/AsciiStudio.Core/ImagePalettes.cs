using System.Globalization;

namespace AsciiStudio.Core;

public static class ImagePalettes
{
    private static readonly uint[] ansi16 = [0xFF000000,0xFFAA0000,0xFF00AA00,0xFFAA5500,0xFF0000AA,0xFFAA00AA,0xFF00AAAA,0xFFAAAAAA,
        0xFF555555,0xFFFF5555,0xFF55FF55,0xFFFFFF55,0xFF5555FF,0xFFFF55FF,0xFF55FFFF,0xFFFFFFFF];
    private static readonly uint[] ansi256 = CreateAnsi256();
    public static ReadOnlySpan<uint> Ansi16 => ansi16;
    public static ReadOnlySpan<uint> Ansi256 => ansi256;
    private static uint[] CreateAnsi256()
    {
        var result = new uint[256]; ansi16.CopyTo(result, 0); int[] levels = [0, 95, 135, 175, 215, 255];
        for (var r = 0; r < 6; r++) for (var g = 0; g < 6; g++) for (var b = 0; b < 6; b++)
            result[16 + 36 * r + 6 * g + b] = Pack(levels[r], levels[g], levels[b]);
        for (var i = 0; i < 24; i++) result[232 + i] = Pack(8 + i * 10, 8 + i * 10, 8 + i * 10);
        return result;
    }
    public static uint Pack(int r, int g, int b) => 0xFF000000u | (uint)Math.Clamp(r, 0, 255) << 16 | (uint)Math.Clamp(g, 0, 255) << 8 | (uint)Math.Clamp(b, 0, 255);
    public static uint[] Parse(string colors)
    {
        if (colors is null || colors.Length > 1024) throw new ArgumentException("调色板最多64个 #RRGGBB 颜色。");
        var entries = colors.Split([',', ';', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length is < 2 or > 64) throw new ArgumentException("调色板需要2到64个 #RRGGBB 颜色。");
        return entries.Select(ParseColor).ToArray();
    }
    public static uint ParseColor(string text)
    {
        if (text is null || text.Length != 7 || text[0] != '#' || !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException("颜色格式应为 #RRGGBB。");
        return value | 0xFF000000;
    }
    public static int Nearest(uint color, ReadOnlySpan<uint> palette)
    {
        if (palette.Length == 0) throw new ArgumentException("调色板不能为空。");
        var index = 0; var minimum = double.MaxValue;
        for (var i = 0; i < palette.Length; i++)
        {
            var r = (int)(color >> 16 & 255) - (int)(palette[i] >> 16 & 255);
            var g = (int)(color >> 8 & 255) - (int)(palette[i] >> 8 & 255);
            var b = (int)(color & 255) - (int)(palette[i] & 255);
            var distance = .2126 * r * r + .7152 * g * g + .0722 * b * b;
            if (distance < minimum) { minimum = distance; index = i; }
        }
        return index;
    }
    public static uint[] Gradient(uint[] stops, int count)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Length is < 2 or > 64 || count is < 2 or > 64) throw new ArgumentException("渐变需要2到64个颜色。");
        return Enumerable.Range(0, count).Select(i =>
        {
            var position = i / (double)(count - 1) * (stops.Length - 1); var low = Math.Min(stops.Length - 2, (int)position); var mix = position - low;
            int Channel(int shift) => (int)Math.Round((stops[low] >> shift & 255) * (1 - mix) + (stops[low + 1] >> shift & 255) * mix);
            return Pack(Channel(16), Channel(8), Channel(0));
        }).ToArray();
    }
    private sealed record Point(int R, int G, int B, long Count);
    public static uint[] Reduce(uint[] colors, int count, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (count is < 2 or > 64) throw new ArgumentOutOfRangeException(nameof(count));
        var histogram = new Dictionary<int, (long R, long G, long B, long Count)>();
        for (var i = 0; i < colors.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            var c = colors[i]; if (c >> 24 == 0) continue;
            var r = (int)(c >> 16 & 255); var g = (int)(c >> 8 & 255); var b = (int)(c & 255); var key = (r >> 3) << 10 | (g >> 3) << 5 | b >> 3;
            var old = histogram.GetValueOrDefault(key); histogram[key] = (old.R + r, old.G + g, old.B + b, old.Count + 1);
        }
        var points = histogram.Values.Select(v => new Point((int)(v.R / v.Count), (int)(v.G / v.Count), (int)(v.B / v.Count), v.Count)).ToList();
        if (points.Count == 0) return [0xFF000000];
        var boxes = new List<List<Point>> { points };
        int Range(List<Point> box, Func<Point, int> channel) => box.Max(channel) - box.Min(channel);
        while (boxes.Count < count)
        {
            token.ThrowIfCancellationRequested();
            var box = boxes.Where(b => b.Count > 1).OrderByDescending(b => Math.Max(Range(b, p => p.R), Math.Max(Range(b, p => p.G), Range(b, p => p.B)))).FirstOrDefault();
            if (box is null) break;
            var red = Range(box, p => p.R); var green = Range(box, p => p.G); var blue = Range(box, p => p.B);
            Func<Point, int> channel = red >= green && red >= blue ? p => p.R : green >= blue ? p => p.G : p => p.B;
            box.Sort((a, b) => channel(a).CompareTo(channel(b)));
            var half = box.Sum(p => p.Count) / 2; long weight = 0; var split = 0;
            do { weight += box[split++].Count; } while (split < box.Count - 1 && weight < half);
            boxes.Remove(box); boxes.Add(box.Take(split).ToList()); boxes.Add(box.Skip(split).ToList());
        }
        return boxes.Select(box =>
        {
            var total = box.Sum(p => p.Count);
            return Pack((int)(box.Sum(p => p.R * p.Count) / total), (int)(box.Sum(p => p.G * p.Count) / total), (int)(box.Sum(p => p.B * p.Count) / total));
        }).Distinct().ToArray();
    }
}
