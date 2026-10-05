using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Charloom.Core;

namespace Charloom.Services;

public sealed record TextRasterOptions(string Family, int Columns = 120, int Style = 0, bool Bold = true, double Stroke = 0, bool Filled = true);
public static class TextRasterService
{
    public static void ValidateOptions(TextRasterOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Family) || options.Family.Length > 128 || options.Family.Any(char.IsControl)
            || options.Columns is < 16 or > 600 || options.Style is < 0 or > 4 || !double.IsFinite(options.Stroke)
            || options.Stroke is < 0 or > 12 || !options.Filled && options.Stroke == 0)
            throw new ArgumentException("系统字体样式无效；空心字需要大于0的描边。");
    }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern uint GetGlyphIndicesW(IntPtr dc, string text, int count, [Out] ushort[] glyphs, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    public static string[] Missing(string text, string family, bool bold)
    {
        using var font = new Font(family, 32, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var canvas = new Bitmap(1, 1); using var graphics = Graphics.FromImage(canvas);
        var handle = font.ToHfont(); var dc = graphics.GetHdc(); var old = SelectObject(dc, handle);
        try
        {
            var missing = new List<string>();
            foreach (var element in TextArtLayout.Elements(text).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct())
            {
                var indices = new ushort[element.Length]; var status = GetGlyphIndicesW(dc, element, element.Length, indices, 1);
                if (status == uint.MaxValue || indices.Contains((ushort)0xFFFF)) missing.Add(element);
            }
            return missing.Take(40).ToArray();
        }
        finally { SelectObject(dc, old); graphics.ReleaseHdc(dc); DeleteObject(handle); }
    }
    private static (byte[] pixels, int width, int height) Raster(string line, TextRasterOptions options, int spacing)
    {
        ValidateOptions(options);
        if (!FontCatalog.Names.Contains(options.Family)) throw new ArgumentException("所选系统字体未安装，请选择其他字体。");
        using var font = new Font(options.Family, 96, options.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var probe = new Bitmap(1, 1); using var graphics = Graphics.FromImage(probe);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone(); format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        using var path = new GraphicsPath(); float x = 20;
        foreach (var element in TextArtLayout.Elements(line))
        {
            path.AddString(element, font.FontFamily, (int)font.Style, 96, new PointF(x, 20), format);
            x += graphics.MeasureString(element, font, PointF.Empty, format).Width + spacing * 4;
        }
        var width = Math.Max(1, (int)Math.Ceiling(x + 20 + options.Stroke)); var height = (int)Math.Ceiling(font.GetHeight(graphics) + 40 + options.Stroke);
        if (width > 32767 || (long)width * height > 20_000_000) throw new ArgumentException("文字画布过大，请减少内容或字距。");
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var draw = Graphics.FromImage(bitmap))
        {
            draw.Clear(Color.White); draw.SmoothingMode = SmoothingMode.AntiAlias;
            if (options.Filled) draw.FillPath(Brushes.Black, path);
            if (options.Stroke > 0) { using var pen = new Pen(Color.Black, (float)options.Stroke) { LineJoin = LineJoin.Round }; draw.DrawPath(pen, path); }
        }
        return ImagingService.Pixels(bitmap);
    }
    public static string Render(string text, TextArtOptions layout, TextRasterOptions options, double cellAspect, CancellationToken token = default)
    {
        layout.Validate();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new ArgumentException("请输入1到2000个字符。");
        var lines = TextUtilities.Normalize(text).Split('\n');
        var longest = lines.Max(line => Raster(line, options, layout.LetterSpacing).width);
        var chars = options.Style switch { 1 => " .#", 2 => " ·●", 3 => " ░▒▓█", 4 => " .:-=+*#%@", _ => " @" };
        string Draw(string line)
        {
            token.ThrowIfCancellationRequested(); if (line.Length == 0) return "";
            var raster = Raster(line, options, layout.LetterSpacing);
            var col = Math.Clamp((int)Math.Round(options.Columns * (double)raster.width / longest), 8, 600);
            return Charloom.Core.ImageConverter.Convert(raster.pixels, raster.width, raster.height, new ConversionOptions { Columns = col, Characters = chars, CellAspect = cellAspect }, token).Text;
        }
        return TextArtLayout.Render(text, Draw, layout with { Vertical = ArtPacking.Full }, 0, token);
    }
}
