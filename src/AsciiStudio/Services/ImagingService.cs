using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using AsciiStudio.Core;

namespace AsciiStudio.Services;

public static class ImagingService
{
    public static byte[] Thumbnail(byte[] rgba, int width, int height, int maximumSide = 300)
    {
        if (maximumSide is < 1 or > 2400 || width is < 1 or > 32767 || height is < 1 or > 32767
            || (long)width * height > 80_000_000 || rgba.LongLength != (long)width * height * 4)
            throw new ArgumentException("预览图像尺寸或 RGBA 数据无效。");
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var p = (y * width + x) * 4; var s = x * 4;
                    row[s] = rgba[p + 2]; row[s + 1] = rgba[p + 1]; row[s + 2] = rgba[p]; row[s + 3] = rgba[p + 3];
                }
                Marshal.Copy(row, 0, locked.Scan0 + y * locked.Stride, row.Length);
            }
        }
        finally { bitmap.UnlockBits(locked); }
        var scale = Math.Min(1d, maximumSide / (double)Math.Max(width, height));
        using var preview = new Bitmap(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(preview))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(bitmap, new Rectangle(0, 0, preview.Width, preview.Height));
        }
        using var stream = new MemoryStream(); preview.Save(stream, ImageFormat.Png); return stream.ToArray();
    }

    public static (byte[] pixels, int width, int height) Decode(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var original = System.Drawing.Image.FromStream(stream, true, true);
        if ((long)original.Width * original.Height > 80_000_000) throw new InvalidDataException("图片超过 8000 万像素，请先降低分辨率。");
        if (original.PropertyIdList.Contains(0x112))
        {
            var value = original.GetPropertyItem(0x112)?.Value;
            if (value is { Length: >= 2 }) original.RotateFlip(BitConverter.ToUInt16(value) switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            });
        }
        var scale = Math.Min(1d, 2400d / Math.Max(original.Width, original.Height));
        using var bitmap = new Bitmap(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)), PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(original, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        }
        return Pixels(bitmap);
    }

    public static (byte[] pixels, int width, int height) RasterizeText(string text, string family, float fontSize, bool bold)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("请输入要转换的文字。");
        if (text.Length > 2000) throw new ArgumentException("字体转换模式最多支持 2000 个字符。");
        using var font = new Font(family, fontSize, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var probe = new Bitmap(1, 1);
        using var pg = Graphics.FromImage(probe);
        var size = pg.MeasureString(text, font, 4000, StringFormat.GenericTypographic);
        var width = (int)Math.Ceiling(size.Width) + 40; var height = (int)Math.Ceiling(size.Height) + 40;
        if ((long)width * height > 20_000_000) throw new ArgumentException("文字画布过大，请减少内容或字号。");
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White); g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawString(text, font, Brushes.Black, new RectangleF(20, 20, width - 40, height - 40), StringFormat.GenericTypographic);
        }
        return Pixels(bitmap);
    }

    public static (byte[] pixels, int width, int height) Pixels(Bitmap bitmap)
    {
        var result = new byte[bitmap.Width * bitmap.Height * 4];
        var locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[bitmap.Width * 4];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(locked.Scan0 + y * locked.Stride, row, 0, row.Length);
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var p = (y * bitmap.Width + x) * 4; var s = x * 4;
                    result[p] = row[s + 2]; result[p + 1] = row[s + 1]; result[p + 2] = row[s]; result[p + 3] = row[s + 3];
                }
            }
        }
        finally { bitmap.UnlockBits(locked); }
        return (result, bitmap.Width, bitmap.Height);
    }

    public static (int Width, int Height) RenderSize(AsciiDocument document, float size = 14, int padding = 20, int scale = 1)
    {
        document = UnicodeGrid.Upgrade(document);
        if (!float.IsFinite(size) || size is < 1 or > 120 || scale is < 1 or > 4 || padding is < 0 or > 200) throw new ArgumentException("导出字号或倍率无效。");
        using var font = new Font(document.FontFamily, size * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var measure = new Bitmap(1, 1); using var g = Graphics.FromImage(measure);
        var cell = g.MeasureString("M", font, new PointF(0, 0), StringFormat.GenericTypographic).Width;
        var width = Math.Max(1, checked((int)Math.Ceiling(document.Width * cell) + padding * scale * 2));
        var height = Math.Max(1, checked((int)Math.Ceiling(document.Height * font.GetHeight(g)) + padding * scale * 2));
        if ((long)width * height > 40_000_000 || width > 32767 || height > 32767) throw new ArgumentException("输出图片超过 4000 万像素或单边 32767 像素，请降低字号、倍率或字符网格尺寸。");
        return (width, height);
    }
    public static byte[] Render(AsciiDocument document, float size = 14, int padding = 20, bool transparent = false, ImageFormat? format = null, int scale = 1)
    {
        document = UnicodeGrid.Upgrade(document);
        var dimensions = RenderSize(document, size, padding, scale); size *= scale; padding *= scale;
        using var font = new Font(document.FontFamily, size, FontStyle.Regular, GraphicsUnit.Pixel);
        using var measure = new Bitmap(1, 1); using var mg = Graphics.FromImage(measure);
        var cell = mg.MeasureString("M", font, new PointF(0, 0), StringFormat.GenericTypographic).Width;
        var monospaced = new[] { "i", "W", "0", " " }.All(glyph => Math.Abs(mg.MeasureString(glyph, font, new PointF(0, 0), StringFormat.GenericTypographic).Width - cell) < .05f);
        var lineHeight = font.GetHeight(mg);
        var width = dimensions.Width;
        var height = dimensions.Height;
        if ((long)width * height > 40_000_000) throw new ArgumentException("输出图片超过 4000 万像素，请降低字号或字符画尺寸。");
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(transparent ? Color.Transparent : Color.FromArgb(255, 18, 24, 34));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var lines = document.Text.Split('\n');
            for (var y = 0; y < lines.Length; y++)
            {
                if (document.BackgroundColors is not null)
                    for (var x = 0; x < document.Width;)
                    {
                        var start = x; var color = document.BackgroundColors[y * document.Width + x];
                        while (x < document.Width && document.BackgroundColors[y * document.Width + x] == color) x++;
                        using var brush = new SolidBrush(Color.FromArgb(unchecked((int)color)));
                        var left = padding + start * cell; var right = padding + x * cell;
                        g.FillRectangle(brush, (float)Math.Floor(left), (float)Math.Floor(padding + y * lineHeight), (float)Math.Ceiling(right) - (float)Math.Floor(left), (float)Math.Ceiling(padding + (y + 1) * lineHeight) - (float)Math.Floor(padding + y * lineHeight));
                    }
                if (monospaced && document.Colors is null && lines[y].All(char.IsAscii))
                {
                    using var brush = new SolidBrush(Color.FromArgb(231, 237, 247));
                    g.DrawString(lines[y], font, brush, padding, padding + y * lineHeight, StringFormat.GenericTypographic);
                }
                else if (monospaced && document.Colors is not null && lines[y].All(char.IsAscii))
                {
                    for (var x = 0; x < lines[y].Length;)
                    {
                        var start = x; var color = document.Colors[y * document.Width + x];
                        while (x < lines[y].Length && document.Colors[y * document.Width + x] == color) x++;
                        using var brush = new SolidBrush(Color.FromArgb(unchecked((int)color)));
                        g.DrawString(lines[y][start..x], font, brush, padding + start * cell, padding + y * lineHeight, StringFormat.GenericTypographic);
                    }
                }
                else
                {
                    foreach (var glyph in UnicodeGrid.Glyphs(lines[y]))
                    {
                        if (glyph.Width == 0) continue;
                        var color = document.Colors?[y * document.Width + glyph.Column] ?? 0xFFE7EDF7;
                        using var brush = new SolidBrush(Color.FromArgb(unchecked((int)color)));
                        GlyphPainter.Draw(g, glyph.Text, font, brush, padding + glyph.Column * cell, padding + y * lineHeight, cell, lineHeight, transparent ? Color.Transparent : Color.FromArgb(255, 18, 24, 34));
                    }
                }
            }
        }
        using var stream = new MemoryStream(); bitmap.Save(stream, format ?? ImageFormat.Png); return stream.ToArray();
    }
}
