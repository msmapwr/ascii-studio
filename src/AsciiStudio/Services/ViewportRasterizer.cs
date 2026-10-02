using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using AsciiStudio.Core;

namespace AsciiStudio.Services;

public static class ViewportRasterizer
{
    public static byte[] Render(DocumentViewIndex index, float size, double zoom, ViewportRegion region, CancellationToken cancellation)
    {
        if (!float.IsFinite(size) || size is < 1 or > 120 || !double.IsFinite(zoom) || zoom <= 0 || zoom > 8)
            throw new ArgumentException("预览缩放参数无效。");
        if (!new[] { region.X, region.Y, region.Width, region.Height, region.Density }.All(double.IsFinite)
            || region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 || region.Density <= 0
            || region.Width * region.Density > 4096 || region.Height * region.Density > 4096
            || (long)region.PixelWidth * region.PixelHeight > 4_000_000)
            throw new ArgumentException("预览区域超过资源预算。");
        cancellation.ThrowIfCancellationRequested();
        var document = index.Document;
        using var font = new Font(document.FontFamily, size, FontStyle.Regular, GraphicsUnit.Pixel);
        using var bitmap = new Bitmap(region.PixelWidth, region.PixelHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var cell = graphics.MeasureString("MMMMMMMM", font, PointF.Empty, StringFormat.GenericTypographic).Width / 8;
        var lineHeight = font.GetHeight(graphics);
        graphics.Clear(Color.FromArgb(255, 18, 24, 34));
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.TranslateTransform((float)(-region.X * region.Density), (float)(-region.Y * region.Density));
        graphics.ScaleTransform((float)(region.Density * zoom), (float)(region.Density * zoom));
        var firstRow = Math.Clamp((int)Math.Floor((region.Y / zoom - 20) / lineHeight), 0, Math.Max(0, document.Height - 1));
        var lastRow = Math.Clamp((int)Math.Ceiling(((region.Y + region.Height) / zoom - 20) / lineHeight) + 1, firstRow, document.Height);
        var firstColumn = Math.Clamp((int)Math.Floor((region.X / zoom - 20) / cell) - 2, 0, document.Width);
        var lastColumn = Math.Clamp((int)Math.Ceiling(((region.X + region.Width) / zoom - 20) / cell) + 2, 0, document.Width);
        var visibleCells = (long)(lastRow - firstRow) * (lastColumn - firstColumn);
        var sampleStep = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(visibleCells / 100_000d)));
        for (var row = firstRow; row < lastRow; row += sampleStep)
        {
            cancellation.ThrowIfCancellationRequested();
            var line = index.Line(row);
            if (document.BackgroundColors is not null)
                for (var column = firstColumn; column < lastColumn;)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var start = column; var color = document.BackgroundColors[row * document.Width + column];
                    if (sampleStep == 1)
                        while (column < lastColumn && document.BackgroundColors[row * document.Width + column] == color) column++;
                    else column = Math.Min(lastColumn, column + sampleStep);
                    using var brush = new SolidBrush(Color.FromArgb(unchecked((int)color)));
                    graphics.FillRectangle(brush, 20 + start * cell, 20 + row * lineHeight, (column - start) * cell, Math.Min(sampleStep, lastRow - row) * lineHeight);
                }
            if (line.All(char.IsAscii))
            {
                var end = Math.Min(lastColumn, line.Length);
                for (var column = Math.Min(firstColumn, end); column < end;)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var start = column; var color = document.Colors?[row * document.Width + column] ?? 0xFFE7EDF7;
                    if (sampleStep > 1)
                    {
                        column = Math.Min(end, column + sampleStep);
                        if (!char.IsWhiteSpace(line[start]))
                        {
                            using var sampleBrush = new SolidBrush(Color.FromArgb(150, Color.FromArgb(unchecked((int)color))));
                            graphics.FillRectangle(sampleBrush, 20 + start * cell, 20 + row * lineHeight, (column - start) * cell, Math.Min(sampleStep, lastRow - row) * lineHeight);
                        }
                    }
                    else
                    {
                        while (column < end && (document.Colors?[row * document.Width + column] ?? 0xFFE7EDF7) == color) column++;
                        using var brush = new SolidBrush(Color.FromArgb(unchecked((int)color)));
                        graphics.DrawString(line[start..column], font, brush, 20 + start * cell, 20 + row * lineHeight, StringFormat.GenericTypographic);
                    }
                }
            }
            else foreach (var glyph in UnicodeGrid.Glyphs(line))
            {
                cancellation.ThrowIfCancellationRequested();
                if (glyph.Column >= lastColumn) break;
                if (glyph.Width == 0 || glyph.Column + glyph.Width <= firstColumn || (sampleStep > 1 && glyph.Column % sampleStep != 0)) continue;
                using var brush = new SolidBrush(Color.FromArgb(unchecked((int)(document.Colors?[row * document.Width + glyph.Column] ?? 0xFFE7EDF7))));
                if (sampleStep > 1)
                {
                    if (!string.IsNullOrWhiteSpace(glyph.Text)) graphics.FillRectangle(brush, 20 + glyph.Column * cell, 20 + row * lineHeight,
                        Math.Min(sampleStep, lastColumn - glyph.Column) * cell, Math.Min(sampleStep, lastRow - row) * lineHeight);
                }
                else GlyphPainter.Draw(graphics, glyph.Text, font, brush, 20 + glyph.Column * cell, 20 + row * lineHeight, cell, lineHeight);
            }
        }
        cancellation.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); return stream.ToArray();
    }
}
