using System.Text;

namespace Charloom.Core;

/// <summary>Platform-independent RGBA converter. Each output cell averages its entire source region.</summary>
public static class ImageConverter
{
    public static AsciiDocument Convert(byte[] rgba, int width, int height, ConversionOptions options, CancellationToken cancellationToken = default, GlyphCoverage[]? glyphs = null)
    {
        ImageQualityConverter.Validate(options);
        ArgumentNullException.ThrowIfNull(rgba); cancellationToken.ThrowIfCancellationRequested();
        if (options.Style != ImageArtStyle.Density || options.MeasureGlyphDensity || options.AdaptiveStrength != 0 || options.PreserveTransparent || options.TrimTransparent || options.PaletteMode != ImagePaletteMode.Original)
            return ImageQualityConverter.Convert(rgba, width, height, options, glyphs, cancellationToken);
        if (width <= 0 || height <= 0 || (long)width * height > 80_000_000 || (long)width * height * 4 != rgba.LongLength)
            throw new ArgumentException("图片像素数据无效。");
        if (options.Columns is < 8 or > 2000 || options.Rows is < 0 or > 2000 || !double.IsFinite(options.CellAspect) || options.CellAspect is <= 0 or > 2 || !double.IsFinite(options.Gamma) || options.Gamma is <= 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(options), "输出尺寸、比例或 Gamma 无效。");
        if (!new[] { options.Brightness, options.Contrast, options.Saturation, options.Hue, options.Grayscale, options.Sepia, options.Sharpness }.All(double.IsFinite) || options.Brightness is < 0 or > 5 || options.Contrast is < 0 or > 5 || options.Saturation is < 0 or > 5 || options.Grayscale is < 0 or > 1 || options.Sepia is < 0 or > 1 || options.Sharpness is < 0 or > 10 || options.ThresholdValue is < 0 or > 255 || !Enum.IsDefined(options.Dither))
            throw new ArgumentException("图片调整参数无效。");
        var chars = options.Characters.Distinct().ToArray();
        if (chars.Length < 2 || chars.Any(char.IsControl) || chars.Any(char.IsSurrogate) || chars.Any(c => UnicodeGrid.GlyphWidth(c.ToString()) != 1))
            throw new ArgumentException("图片字符集至少需要两个不同的单列字符；中文、emoji 和组合标记请用于文字创作。");
        var columns = options.Columns;
        var requestedRows = options.Rows == 0 ? Math.Max(1, Math.Round(columns * (double)height / width * options.CellAspect)) : options.Rows;
        if (requestedRows > 2000 || columns * requestedRows > 4_000_000)
            throw new ArgumentException("输出分辨率超过 2000 行或 400 万字符，请降低列数或指定行数。");
        var rows = (int)requestedRows;
        var luminance = new double[rows * columns];
        var colors = options.Color ? new uint[rows * columns] : null;
        for (var y = 0; y < rows; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < columns; x++)
            {
                var (r, g, b) = SampleCell(rgba, width, height, columns, rows, x, y, options.Background, cancellationToken);
                (r, g, b) = AdjustColor(r, g, b, options);
                luminance[y * columns + x] = 1 - (.2126 * r + .7152 * g + .0722 * b);
                if (colors is not null) colors[y * columns + x] = 0xFF000000u | (uint)(r * 255) << 16 | (uint)(g * 255) << 8 | (uint)(b * 255);
            }
        }
        ApplyEdges(luminance, columns, rows, options, cancellationToken);
        var indices = Dither(luminance, columns, rows, chars.Length, options, cancellationToken);
        return Map(indices, chars, columns, rows, colors, cancellationToken);
    }

    // Sample and adjust one cell at a time to avoid an extra RGB buffer for million-cell results.
    private static (double, double, double) SampleCell(byte[] rgba, int width, int height, int columns,
        int rows, int x, int y, uint background, CancellationToken cancellationToken)
    {
        double br = (background >> 16) & 255, bg = (background >> 8) & 255, bb = background & 255;
        int x0 = (int)((long)x * width / columns), x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * width / columns));
        int y0 = (int)((long)y * height / rows), y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * height / rows));
        double r = 0, g = 0, b = 0; var count = 0;
        for (var sy = y0; sy < Math.Min(y1, height); sy++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var sx = x0; sx < Math.Min(x1, width); sx++)
            {
                var p = (sy * width + sx) * 4; var a = rgba[p + 3] / 255d;
                r += rgba[p] * a + br * (1 - a); g += rgba[p + 1] * a + bg * (1 - a); b += rgba[p + 2] * a + bb * (1 - a); count++;
            }
        }
        r /= count; g /= count; b /= count;
        return (r, g, b);
    }

    private static (double, double, double) AdjustColor(double r, double g, double b, ConversionOptions options)
    {
        var gray = .2126 * r + .7152 * g + .0722 * b;
        r = gray + (r - gray) * options.Saturation; g = gray + (g - gray) * options.Saturation; b = gray + (b - gray) * options.Saturation;
        if (options.Hue != 0) (r, g, b) = RotateHue(r, g, b, options.Hue);
        gray = .2126 * r + .7152 * g + .0722 * b;
        r += (gray - r) * options.Grayscale; g += (gray - g) * options.Grayscale; b += (gray - b) * options.Grayscale;
        var sr = .393 * r + .769 * g + .189 * b; var sg = .349 * r + .686 * g + .168 * b; var sb = .272 * r + .534 * g + .131 * b;
        r += (sr - r) * options.Sepia; g += (sg - g) * options.Sepia; b += (sb - b) * options.Sepia;
        double Adjust(double v)
        {
            var adjusted = Math.Clamp(((v / 255 - .5) * options.Contrast + .5) * options.Brightness, 0, 1);
            return options.Gamma == 1 ? adjusted : Math.Pow(adjusted, 1 / options.Gamma);
        }
        r = Adjust(r); g = Adjust(g); b = Adjust(b);
        if (options.Invert) { r = 1 - r; g = 1 - g; b = 1 - b; }
        return (r, g, b);
    }

    private static void ApplyEdges(double[] luminance, int columns, int rows, ConversionOptions options, CancellationToken cancellationToken)
    {
        if (options.Sharpness > 0 || options.Edges)
        {
            var original = (double[])luminance.Clone();
            for (var y = 1; y < rows - 1; y++) for (var x = 1; x < columns - 1; x++)
            {
                var i = y * columns + x;
                if (x == 1) cancellationToken.ThrowIfCancellationRequested();
                var neighbors = (original[i - 1] + original[i + 1] + original[i - columns] + original[i + columns]) / 4;
                luminance[i] = options.Edges ? Math.Clamp(Math.Abs(original[i - 1] - original[i + 1]) + Math.Abs(original[i - columns] - original[i + columns]), 0, 1)
                    : Math.Clamp(original[i] + (original[i] - neighbors) * options.Sharpness, 0, 1);
            }
        }
    }

    private static ushort[] Dither(double[] luminance, int columns, int rows, int levels, ConversionOptions options, CancellationToken cancellationToken)
    {
        var kernel = Kernel(options.Dither);
        // A ramp consists of distinct UTF-16 chars, so a 16-bit index is sufficient.
        var indices = new ushort[luminance.Length];
        for (var y = 0; y < rows; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < columns; x++)
            {
                var value = Math.Clamp(luminance[y * columns + x], 0, 1);
                if (options.Threshold) value = value >= 1 - options.ThresholdValue / 255d ? 1 : 0;
                var index = (int)Math.Round(value * (levels - 1)); indices[y * columns + x] = (ushort)index;
                var error = value - index / (double)(levels - 1);
                foreach (var (dx, dy, weight) in kernel)
                    if (x + dx >= 0 && x + dx < columns && y + dy < rows) luminance[(y + dy) * columns + x + dx] += error * weight;
            }
        }
        return indices;
    }

    private static AsciiDocument Map(ushort[] indices, char[] chars, int columns, int rows, uint[]? colors, CancellationToken cancellationToken)
    {
        var result = new StringBuilder(rows * (columns + 1));
        for (var y = 0; y < rows; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < columns; x++) result.Append(chars[indices[y * columns + x]]);
            if (y < rows - 1) result.Append('\n');
        }
        return new AsciiDocument { GridVersion = 1, Text = result.ToString(), Width = columns, Height = rows, Colors = colors };
    }

    internal static (double, double, double) RotateHue(double r, double g, double b, double angle)
    {
        var radians = (angle % 360) * Math.PI / 180;
        var c = Math.Cos(radians); var s = Math.Sin(radians);
        return ((.213 + .787 * c - .213 * s) * r + (.715 - .715 * c - .715 * s) * g + (.072 - .072 * c + .928 * s) * b,
            (.213 - .213 * c + .143 * s) * r + (.715 + .285 * c + .140 * s) * g + (.072 - .072 * c - .283 * s) * b,
            (.213 - .213 * c - .787 * s) * r + (.715 - .715 * c + .715 * s) * g + (.072 + .928 * c + .072 * s) * b);
    }

    internal static (int x, int y, double weight)[] Kernel(DitherMode mode) => mode switch
    {
        DitherMode.FloydSteinberg => [(1, 0, 7d / 16), (-1, 1, 3d / 16), (0, 1, 5d / 16), (1, 1, 1d / 16)],
        DitherMode.Atkinson => [(1, 0, 1d / 8), (2, 0, 1d / 8), (-1, 1, 1d / 8), (0, 1, 1d / 8), (1, 1, 1d / 8), (0, 2, 1d / 8)],
        DitherMode.JarvisJudiceNinke => [(1, 0, 7d / 48), (2, 0, 5d / 48), (-2, 1, 3d / 48), (-1, 1, 5d / 48), (0, 1, 7d / 48), (1, 1, 5d / 48), (2, 1, 3d / 48), (-2, 2, 1d / 48), (-1, 2, 3d / 48), (0, 2, 5d / 48), (1, 2, 3d / 48), (2, 2, 1d / 48)],
        DitherMode.Stucki => [(1, 0, 8d / 42), (2, 0, 4d / 42), (-2, 1, 2d / 42), (-1, 1, 4d / 42), (0, 1, 8d / 42), (1, 1, 4d / 42), (2, 1, 2d / 42), (-2, 2, 1d / 42), (-1, 2, 2d / 42), (0, 2, 4d / 42), (1, 2, 2d / 42), (2, 2, 1d / 42)],
        _ => []
    };
}
