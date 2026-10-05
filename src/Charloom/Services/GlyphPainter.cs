using System.Drawing;
using Charloom.Core;

namespace Charloom.Services;

internal static class GlyphPainter
{
    public static void Draw(Graphics graphics, string glyph, Font font, Brush brush, float x, float y, float width, float height, Color? canvas = null)
    {
        if (ImageQualityConverter.BlockFill(glyph) is { } block)
        {
            // Two source halves are independent: a translucent upper half
            // must not blend against the lower half's background color.
            if (brush is SolidBrush foreground && foreground.Color.A < 255)
            {
                using var baseBrush = new SolidBrush(canvas ?? Color.FromArgb(255, 18, 24, 34));
                var mode = graphics.CompositingMode; graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.FillRectangle(baseBrush, x, y + (float)block.Top * height, width, (float)block.Height * height);
                graphics.CompositingMode = mode;
            }
            graphics.FillRectangle(brush, x, y + (float)block.Top * height, width, (float)block.Height * height);
        }
        else graphics.DrawString(glyph, font, brush, x, y, StringFormat.GenericTypographic);
    }
}
