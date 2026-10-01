using System.Drawing;
using System.Drawing.Text;

namespace AsciiStudio.Services;

public static class FontCatalog
{
    public static string[] Names { get; } = Load();
    private static readonly Dictionary<string, (double Width, double Height)> metrics = new(StringComparer.OrdinalIgnoreCase);
    private static string[] Load()
    {
        using var installed = new InstalledFontCollection();
        var families = installed.Families;
        try { return families.Where(f => f.IsStyleAvailable(FontStyle.Regular)).Select(f => f.Name).Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToArray(); }
        finally { foreach (var family in families) family.Dispose(); }
    }
    public static (double Width, double Height) Measure(string family)
    {
        lock (metrics)
        {
            if (metrics.TryGetValue(family, out var value)) return value;
            using var font = new Font(family, 13, FontStyle.Regular, GraphicsUnit.Pixel);
            using var bitmap = new Bitmap(1, 1); using var graphics = Graphics.FromImage(bitmap);
            value = (graphics.MeasureString("MMMMMMMM", font, PointF.Empty, StringFormat.GenericTypographic).Width / 8, font.GetHeight(graphics));
            metrics[family] = value; return value;
        }
    }
    public static bool IsMonospaced(string family)
    {
        using var font = new Font(family, 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var bitmap = new Bitmap(1, 1); using var graphics = Graphics.FromImage(bitmap);
        return Math.Abs(graphics.MeasureString("MMMMMMMM", font, PointF.Empty, StringFormat.GenericTypographic).Width
            - graphics.MeasureString("iiiiiiii", font, PointF.Empty, StringFormat.GenericTypographic).Width) < .1;
    }
    public static bool IsChineseCommon(string name) => new[] { "YaHei", "SimSun", "SimHei", "FangSong", "KaiTi", "DengXian", "CJK", "Chinese", "宋", "黑", "楷" }.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
}
