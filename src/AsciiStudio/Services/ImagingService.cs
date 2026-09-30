using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using AsciiStudio.Core;

namespace AsciiStudio.Services;

public static class ImagingService
{
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
                2 => RotateFlipType.RotateNoneFlipX, 3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX, 5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone, 7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone, _ => RotateFlipType.RotateNoneFlipNone
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

    public static byte[] Render(AsciiDocument document, float size = 14, int padding = 20, bool transparent = false, ImageFormat? format = null)
    {
        document.Validate();
        using var font = new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Pixel);
        using var measure = new Bitmap(1, 1); using var mg = Graphics.FromImage(measure);
        var cell = mg.MeasureString("M", font, new PointF(0, 0), StringFormat.GenericTypographic).Width;
        var lineHeight = font.GetHeight(mg);
        var width = Math.Max(1, (int)Math.Ceiling(document.Width * cell) + padding * 2);
        var height = Math.Max(1, (int)Math.Ceiling(document.Height * lineHeight) + padding * 2);
        if ((long)width * height > 40_000_000) throw new ArgumentException("输出图片超过 4000 万像素，请降低字号或字符画尺寸。");
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(transparent ? Color.Transparent : Color.FromArgb(255, 18, 24, 34));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var lines = document.Text.Split('\n');
            for (var y = 0; y < lines.Length; y++)
            {
                if (document.Colors is null)
                {
                    using var brush = new SolidBrush(Color.FromArgb(231, 237, 247));
                    g.DrawString(lines[y], font, brush, padding, padding + y * lineHeight, StringFormat.GenericTypographic);
                }
                else
                {
                    for (var x = 0; x < lines[y].Length;)
                    {
                        var start = x; var color = document.Colors[y * document.Width + x];
                        while (x < lines[y].Length && document.Colors[y * document.Width + x] == color) x++;
                        using var brush = new SolidBrush(Color.FromArgb(unchecked((int)color)));
                        g.DrawString(lines[y][start..x], font, brush, padding + start * cell, padding + y * lineHeight, StringFormat.GenericTypographic);
                    }
                }
            }
        }
        using var stream = new MemoryStream(); bitmap.Save(stream, format ?? ImageFormat.Png); return stream.ToArray();
    }
}
