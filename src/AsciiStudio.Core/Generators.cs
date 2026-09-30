using System.Text;

namespace AsciiStudio.Core;

public static class Generators
{
    public static string Border(string text, int style)
    {
        var rows = TextUtilities.Normalize(text).Split('\n');
        var width = rows.Max(r => r.Length);
        if (width > 2000 || rows.Length > 1000) throw new ArgumentException("边框内容过大。");
        var (tl, tr, bl, br, h, v) = style switch
        {
            2 => ('╔', '╗', '╚', '╝', '═', '║'),
            3 => ('*', '*', '*', '*', '*', '*'),
            _ => ('+', '+', '+', '+', '-', '|')
        };
        return tl + new string(h, width + 2) + tr + "\n" +
            string.Join('\n', rows.Select(r => $"{v} {r.PadRight(width)} {v}")) + "\n" + bl + new string(h, width + 2) + br;
    }

    public static string Divider(int width, int style) => style switch
    {
        1 => Repeat("─ · ", Math.Clamp(width, 8, 300)),
        2 => Repeat("-=", Math.Clamp(width, 8, 300)),
        3 => Repeat("~*~", Math.Clamp(width, 8, 300)),
        _ => new string('-', Math.Clamp(width, 8, 300))
    };
    private static string Repeat(string pattern, int width) => string.Concat(Enumerable.Repeat(pattern, (width + pattern.Length - 1) / pattern.Length))[..width];

    public static string Maze(int width, int height, int seed)
    {
        width = Math.Clamp(width, 3, 100); height = Math.Clamp(height, 3, 80);
        var cells = new bool[height * 2 + 1, width * 2 + 1];
        var seen = new bool[height, width]; var random = new Random(seed);
        var stack = new Stack<(int x, int y)>(); stack.Push((0, 0)); seen[0, 0] = true; cells[1, 1] = true;
        (int x, int y)[] directions = [(0, -1), (1, 0), (0, 1), (-1, 0)];
        while (stack.Count > 0)
        {
            var (x, y) = stack.Peek();
            var options = directions.Select(d => (x: x + d.x, y: y + d.y)).Where(p => p.x >= 0 && p.x < width && p.y >= 0 && p.y < height && !seen[p.y, p.x]).ToArray();
            if (options.Length == 0) { stack.Pop(); continue; }
            var n = options[random.Next(options.Length)]; seen[n.y, n.x] = true;
            cells[y + n.y + 1, x + n.x + 1] = true; cells[n.y * 2 + 1, n.x * 2 + 1] = true; stack.Push(n);
        }
        cells[1, 0] = true; cells[height * 2 - 1, width * 2] = true;
        var b = new StringBuilder();
        for (var y = 0; y < cells.GetLength(0); y++)
        { for (var x = 0; x < cells.GetLength(1); x++) b.Append(cells[y, x] ? ' ' : '#'); if (y < cells.GetLength(0) - 1) b.Append('\n'); }
        return b.ToString();
    }

    public static string Pattern(int width, int height, int style, int seed)
    {
        width = Math.Clamp(width, 8, 300); height = Math.Clamp(height, 3, 100);
        var random = new Random(seed); var b = new StringBuilder();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++) b.Append(style switch
            {
                0 => random.Next(100) switch { < 2 => '*', < 5 => '+', < 13 => '.', _ => ' ' },
                1 => (x / 3 + y / 2) % 2 == 0 ? '#' : ' ',
                2 => (x + y) % 6 < 3 ? '/' : '\\',
                _ => " .:+*#"[(x + y) % 6]
            });
            if (y < height - 1) b.Append('\n');
        }
        return b.ToString();
    }
}
