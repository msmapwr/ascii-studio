using System.Text;

namespace AsciiStudio.Core;

public sealed record GlyphCoverage(char Glyph, double Coverage);
public sealed record ImageFrame(byte[] Pixels, int Width, int Height);
public sealed record ImageSample(float[] Rgba, int Width, int Height, int Columns, int Rows, int ScaleX, int ScaleY);
public sealed record ImageFiltered(float[] Density, uint[] Colors, int Width, int Height, int Columns, int Rows, int ScaleX, int ScaleY);

/// <summary>Independent sampling, filtering and mapping stages; cached values never retain earlier stages.</summary>
public static class ImageQualityConverter
{
    public static (double Top, double Height)? BlockFill(string glyph) => glyph switch { "▀" => (0, .5), "▄" => (.5, .5), "█" => (0, 1), _ => null };
    public static void Validate(ConversionOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        if (!Enum.IsDefined(o.Style) || !Enum.IsDefined(o.PaletteMode) || !Enum.IsDefined(o.Dither)
            || !double.IsFinite(o.AdaptiveStrength) || o.AdaptiveStrength is < 0 or > 1
            || !double.IsFinite(o.StructureThreshold) || o.StructureThreshold is < 0 or > 1
            || o.AlphaThreshold is < 1 or > 255 || o.PaletteSize is < 2 or > 64)
            throw new ArgumentException("图片质量参数无效。");
        if (o.Characters is null || o.Characters.Length > 200 || o.Style == ImageArtStyle.Density && (o.Characters.Distinct().Count() < 2
            || o.Characters.Any(c => char.IsControl(c) || char.IsSurrogate(c) || UnicodeGrid.GlyphWidth(c.ToString()) != 1)))
            throw new ArgumentException("字符集需要至少两个不同的单列字符。");
        if (o.Columns is < 8 or > 2000 || o.Rows is < 0 or > 2000 || !double.IsFinite(o.CellAspect) || o.CellAspect is <= 0 or > 2
            || !double.IsFinite(o.Gamma) || o.Gamma is <= 0 or > 5
            || !new[] { o.Brightness, o.Contrast, o.Saturation, o.Hue, o.Grayscale, o.Sepia, o.Sharpness }.All(double.IsFinite)
            || o.Brightness is < 0 or > 5 || o.Contrast is < 0 or > 5 || o.Saturation is < 0 or > 5
            || o.Grayscale is < 0 or > 1 || o.Sepia is < 0 or > 1 || o.Sharpness is < 0 or > 10 || o.ThresholdValue is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(o), "输出尺寸或图片调整参数无效。");
        if (o.PaletteMode is ImagePaletteMode.TwoTone or ImagePaletteMode.Custom or ImagePaletteMode.Gradient) ImagePalettes.Parse(o.PaletteColors);
    }

    public static ImageFrame Trim(byte[] pixels, int width, int height, int threshold, CancellationToken token = default)
    {
        ValidatePixels(pixels, width, height);
        if (threshold is < 1 or > 255) throw new ArgumentOutOfRangeException(nameof(threshold));
        var left = width; var top = height; var right = -1; var bottom = -1;
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++) if (pixels[(y * width + x) * 4 + 3] >= threshold)
            { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = y; }
        }
        if (right < 0) return new(new byte[4], 1, 1);
        if (left == 0 && top == 0 && right == width - 1 && bottom == height - 1) return new(pixels, width, height);
        var w = right - left + 1; var h = bottom - top + 1; var output = new byte[w * h * 4];
        for (var y = 0; y < h; y++) { token.ThrowIfCancellationRequested(); Buffer.BlockCopy(pixels, ((top + y) * width + left) * 4, output, y * w * 4, w * 4); }
        return new(output, w, h);
    }

    private static void ValidatePixels(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || (long)width * height > 80_000_000 || (long)width * height * 4 != pixels.LongLength)
            throw new ArgumentException("图片像素数据无效或超过8000万像素。");
    }

    public static ImageSample Sample(byte[] pixels, int width, int height, ConversionOptions o, CancellationToken token = default)
    {
        Validate(o); ValidatePixels(pixels, width, height); token.ThrowIfCancellationRequested();
        var rows = o.Rows == 0 ? Math.Max(1, Math.Round(o.Columns * (double)height / width * o.CellAspect)) : o.Rows;
        var sx = o.Style == ImageArtStyle.Braille ? 2 : 1;
        var sy = o.Style == ImageArtStyle.Braille ? 4 : o.Style == ImageArtStyle.HalfBlock ? 2 : 1;
        if (rows > 2000 || o.Columns * rows * sx * sy > 4_000_000)
            throw new ArgumentException("超过400万采样点：Braille最多50万字符，半块最多200万字符；请降低分辨率。");
        var w = o.Columns * sx; var h = (int)rows * sy; var rgba = new float[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < w; x++)
            {
                var x0 = (int)((long)x * width / w); var x1 = Math.Min(width, Math.Max(x0 + 1, (int)((long)(x + 1) * width / w)));
                var y0 = (int)((long)y * height / h); var y1 = Math.Min(height, Math.Max(y0 + 1, (int)((long)(y + 1) * height / h)));
                double r = 0, g = 0, b = 0, a = 0; var count = 0;
                for (var py = y0; py < y1; py++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var px = x0; px < x1; px++)
                    {
                        var i = (py * width + px) * 4; var alpha = pixels[i + 3] / 255d;
                        r += pixels[i] * alpha; g += pixels[i + 1] * alpha; b += pixels[i + 2] * alpha; a += alpha; count++;
                    }
                }
                var p = (y * w + x) * 4;
                rgba[p] = (float)(r / count); rgba[p + 1] = (float)(g / count); rgba[p + 2] = (float)(b / count); rgba[p + 3] = (float)(a / count);
            }
        }
        return new(rgba, w, h, o.Columns, (int)rows, sx, sy);
    }

    public static ImageFiltered Filter(ImageSample sample, ConversionOptions o, CancellationToken token = default)
    {
        Validate(o); token.ThrowIfCancellationRequested();
        var length = sample.Width * sample.Height; var density = new float[length]; var colors = new uint[length];
        for (var i = 0; i < length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            var p = i * 4; var a = sample.Rgba[p + 3];
            double r = sample.Rgba[p], g = sample.Rgba[p + 1], b = sample.Rgba[p + 2];
            if (o.PreserveTransparent) { if (a > 0) { r /= a; g /= a; b /= a; } }
            else { r += (o.Background >> 16 & 255) * (1 - a); g += (o.Background >> 8 & 255) * (1 - a); b += (o.Background & 255) * (1 - a); a = 1; }
            var gray = .2126 * r + .7152 * g + .0722 * b;
            r = gray + (r - gray) * o.Saturation; g = gray + (g - gray) * o.Saturation; b = gray + (b - gray) * o.Saturation;
            if (o.Hue != 0) (r, g, b) = ImageConverter.RotateHue(r, g, b, o.Hue);
            gray = .2126 * r + .7152 * g + .0722 * b;
            r += (gray - r) * o.Grayscale; g += (gray - g) * o.Grayscale; b += (gray - b) * o.Grayscale;
            var sr = .393 * r + .769 * g + .189 * b; var sg = .349 * r + .686 * g + .168 * b; var sb = .272 * r + .534 * g + .131 * b;
            r += (sr - r) * o.Sepia; g += (sg - g) * o.Sepia; b += (sb - b) * o.Sepia;
            double Adjust(double v) { var n = Math.Clamp(((v / 255 - .5) * o.Contrast + .5) * o.Brightness, 0, 1); if (o.Gamma != 1) n = Math.Pow(n, 1 / o.Gamma); return o.Invert ? 1 - n : n; }
            r = Adjust(r); g = Adjust(g); b = Adjust(b);
            density[i] = (float)(1 - (.2126 * r + .7152 * g + .0722 * b));
            var alpha = (uint)Math.Round(a * 255);
            colors[i] = alpha < o.AlphaThreshold ? 0 : ImagePalettes.Pack((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255)) & 0xFFFFFF | alpha << 24;
        }
        if (o.AdaptiveStrength > 0 || o.Sharpness > 0 || o.Edges)
        {
            var original = (float[])density.Clone(); var w = sample.Width;
            for (var y = 1; y < sample.Height - 1; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 1; x < w - 1; x++)
                {
                    var i = y * w + x; if (colors[i] >> 24 == 0) continue;
                    float Active(int p) => colors[p] >> 24 == 0 ? original[i] : original[p];
                    var left = Active(i - 1); var right = Active(i + 1); var top = Active(i - w); var bottom = Active(i + w);
                    var mean = (left + right + top + bottom) / 4; var edge = Math.Abs(left - right) + Math.Abs(top - bottom);
                    var value = o.Edges ? edge : original[i] + (original[i] - mean) * o.Sharpness;
                    if (o.AdaptiveStrength > 0 && !o.Edges)
                        value += o.AdaptiveStrength * (edge < .12 ? (mean - original[i]) * .5 : original[i] - mean);
                    density[i] = (float)Math.Clamp(value, 0, 1);
                }
            }
        }
        return new(density, colors, sample.Width, sample.Height, sample.Columns, sample.Rows, sample.ScaleX, sample.ScaleY);
    }

    public static AsciiDocument Map(ImageFiltered grid, ConversionOptions o, GlyphCoverage[]? profile = null, CancellationToken token = default)
    {
        Validate(o); token.ThrowIfCancellationRequested();
        var chars = (o.Style == ImageArtStyle.Density ? o.Characters : " .:-=+*#%@").Distinct().ToArray();
        var levels = chars.Select((c, i) => new GlyphCoverage(c, i / (double)(chars.Length - 1))).ToArray();
        if (o.MeasureGlyphDensity && o.Style == ImageArtStyle.Density)
        {
            if (profile is null || profile.Length != chars.Length || !profile.Select(p => p.Glyph).ToHashSet().SetEquals(chars)
                || profile.Any(p => !double.IsFinite(p.Coverage) || p.Coverage is < 0 or > 1)) throw new ArgumentException("所选字体的字符密度测量无效。");
            var sorted = profile.OrderBy(p => p.Coverage).ToArray(); var range = sorted[^1].Coverage - sorted[0].Coverage;
            if (range > .00001) levels = sorted.Select(p => p with { Coverage = (p.Coverage - sorted[0].Coverage) / range }).ToArray();
        }
        var density = (float[])grid.Density.Clone(); var kernel = ImageConverter.Kernel(o.Dither);
        int Nearest(double value)
        {
            var low = 0; var high = levels.Length - 1;
            while (low < high) { var middle = (low + high) / 2; if (levels[middle].Coverage < value) low = middle + 1; else high = middle; }
            return low > 0 && value - levels[low - 1].Coverage <= levels[low].Coverage - value ? low - 1 : low;
        }
        // Quantize on a private buffer: diffusion never changes cached filter data.
        var quantized = new int[density.Length];
        for (var y = 0; y < grid.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < grid.Width; x++)
            {
                var i = y * grid.Width + x; if (grid.Colors[i] >> 24 == 0) continue;
                var value = Math.Clamp(density[i], 0, 1);
                if (o.Threshold) value = value >= 1 - o.ThresholdValue / 255d ? 1 : 0;
                var binary = o.Style is ImageArtStyle.Braille or ImageArtStyle.HalfBlock;
                var index = binary ? value >= 1 - o.ThresholdValue / 255d ? 1 : 0 : Nearest(value);
                quantized[i] = index; var error = value - (binary ? index : levels[index].Coverage);
                foreach (var (dx, dy, weight) in kernel)
                    if (x + dx >= 0 && x + dx < grid.Width && y + dy < grid.Height)
                    {
                        var target = (y + dy) * grid.Width + x + dx;
                        if (grid.Colors[target] >> 24 != 0) density[target] += (float)(error * weight);
                    }
            }
        }
        var colors = o.Color ? new uint[grid.Columns * grid.Rows] : null;
        var backgrounds = o.Color && o.Style == ImageArtStyle.HalfBlock ? new uint[grid.Columns * grid.Rows] : null;
        var text = new StringBuilder(grid.Rows * (grid.Columns + 1));
        int[] dots = [0, 3, 1, 4, 2, 5, 6, 7];
        for (var y = 0; y < grid.Rows; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < grid.Columns; x++)
            {
                var output = y * grid.Columns + x; var i = y * grid.ScaleY * grid.Width + x * grid.ScaleX; char c;
                if (o.Style == ImageArtStyle.Braille)
                {
                    var bits = 0; long r = 0, g = 0, b = 0, a = 0; var count = 0;
                    for (var py = 0; py < 4; py++) for (var px = 0; px < 2; px++)
                    {
                        var p = i + py * grid.Width + px; var color = grid.Colors[p];
                        if (color >> 24 != 0 && quantized[p] != 0) { bits |= 1 << dots[py * 2 + px]; r += color >> 16 & 255; g += color >> 8 & 255; b += color & 255; a += color >> 24; count++; }
                    }
                    c = bits == 0 ? ' ' : (char)(0x2800 + bits);
                    if (colors is not null && count > 0) colors[output] = ImagePalettes.Pack((int)(r / count), (int)(g / count), (int)(b / count)) & 0xFFFFFF | (uint)(a / count) << 24;
                }
                else if (o.Style == ImageArtStyle.HalfBlock)
                {
                    var top = grid.Colors[i]; var bottom = grid.Colors[i + grid.Width];
                    var t = o.Color ? top >> 24 != 0 : top >> 24 != 0 && quantized[i] != 0;
                    var b = o.Color ? bottom >> 24 != 0 : bottom >> 24 != 0 && quantized[i + grid.Width] != 0;
                    c = t ? b && !o.Color ? '█' : '▀' : b ? '▄' : ' ';
                    if (colors is not null) colors[output] = t ? top : bottom;
                    if (backgrounds is not null) backgrounds[output] = t && b ? bottom : 0;
                }
                else
                {
                    c = levels[quantized[i]].Glyph;
                    if (o.Style == ImageArtStyle.Structure)
                    {
                        var gx = grid.Density[y * grid.Width + Math.Min(x + 1, grid.Width - 1)] - grid.Density[y * grid.Width + Math.Max(x - 1, 0)];
                        var gy = grid.Density[Math.Min(y + 1, grid.Height - 1) * grid.Width + x] - grid.Density[Math.Max(y - 1, 0) * grid.Width + x];
                        c = Math.Abs(gx) + Math.Abs(gy) < o.StructureThreshold ? grid.Density[i] >= .75 ? '#' : ' '
                            : Math.Abs(gy) > Math.Abs(gx) * 2 ? '-' : Math.Abs(gx) > Math.Abs(gy) * 2 ? '|' : gx * gy > 0 ? '/' : '\\';
                    }
                    if (grid.Colors[i] >> 24 == 0) c = ' ';
                    if (colors is not null) colors[output] = grid.Colors[i];
                }
                text.Append(c);
            }
            if (y < grid.Rows - 1) text.Append('\n');
        }
        if (colors is not null && o.PaletteMode != ImagePaletteMode.Original)
        {
            var palette = o.PaletteMode switch
            {
                ImagePaletteMode.Ansi16 => ImagePalettes.Ansi16.ToArray(),
                ImagePaletteMode.Ansi256 => ImagePalettes.Ansi256.ToArray(),
                ImagePaletteMode.TwoTone => ImagePalettes.Parse(o.PaletteColors).Take(2).ToArray(),
                ImagePaletteMode.Custom => ImagePalettes.Parse(o.PaletteColors),
                ImagePaletteMode.Gradient => ImagePalettes.Gradient(ImagePalettes.Parse(o.PaletteColors), o.PaletteSize),
                _ => ImagePalettes.Reduce(backgrounds is null ? colors : colors.Concat(backgrounds).ToArray(), o.PaletteSize, token)
            };
            var lookup = new Dictionary<int, uint>();
            void Apply(uint[] entries)
            {
                for (var i = 0; i < entries.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    var c = entries[i]; if (c >> 24 == 0) continue;
                    var key = (int)((c >> 19 & 31) << 10 | (c >> 11 & 31) << 5 | (c >> 3 & 31));
                    if (!lookup.TryGetValue(key, out var mapped))
                    {
                        var center = ImagePalettes.Pack(((key >> 10 & 31) << 3) + 4, ((key >> 5 & 31) << 3) + 4, ((key & 31) << 3) + 4);
                        lookup[key] = mapped = palette[ImagePalettes.Nearest(center, palette)];
                    }
                    entries[i] = mapped & 0xFFFFFF | c & 0xFF000000;
                }
            }
            Apply(colors); if (backgrounds is not null) Apply(backgrounds);
        }
        return new()
        {
            GridVersion = 1,
            Text = text.ToString(),
            Width = grid.Columns,
            Height = grid.Rows,
            Colors = colors,
            BackgroundColors = backgrounds,
            ColorEncoding = o.PaletteMode == ImagePaletteMode.Ansi16 ? AnsiColorEncoding.Ansi16 : o.PaletteMode == ImagePaletteMode.Ansi256 ? AnsiColorEncoding.Ansi256 : AnsiColorEncoding.TrueColor
        };
    }

    public static AsciiDocument Convert(byte[] pixels, int width, int height, ConversionOptions o, GlyphCoverage[]? profile = null, CancellationToken token = default)
    {
        Validate(o);
        var frame = o.TrimTransparent ? Trim(pixels, width, height, o.AlphaThreshold, token) : new ImageFrame(pixels, width, height);
        return Map(Filter(Sample(frame.Pixels, frame.Width, frame.Height, o, token), o, token), o, profile, token);
    }
}
