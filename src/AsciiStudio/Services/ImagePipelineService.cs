using AsciiStudio.Core;
using System.Drawing;
using System.Drawing.Text;

namespace AsciiStudio.Services;

/// <summary>Keys contain revisions and settings only, so eviction also releases large arrays.</summary>
public sealed class ImagePipelineService
{
    private static readonly BudgetCache cache = new();
    private readonly string owner = Guid.NewGuid().ToString("N");
    private sealed record TransformKey(string Revision, ImageGeometry Geometry, bool Trim, int Alpha);
    private sealed record SampleKey(TransformKey Transform, int Columns, int Rows, double Aspect, int ScaleX, int ScaleY);
    private sealed record FilterKey(SampleKey Sample, double Brightness, double Contrast, double Gamma, double Saturation, double Hue,
        double Gray, double Sepia, double Sharpness, bool Invert, bool Edges, double Adaptive, uint Background, bool Preserve, int Alpha);
    private sealed record ResultKey(TransformKey Transform, ConversionOptions Options, string Family);
    private sealed record PreviewKey(TransformKey Transform, int Size);
    private int hits;
    public int Hits => Volatile.Read(ref hits);
    public long RetainedBytes => cache.OwnerBytes(owner);
    public void Clear() { cache.RemoveOwner(owner); Interlocked.Exchange(ref hits, 0); }
    private T? Get<T>(object key) where T : class
    {
        var value = cache.Get<T>(owner, key); if (value is not null) Interlocked.Increment(ref hits); return value;
    }
    private void Put(object key, object value, long bytes) => cache.Put(owner, key, value, checked(bytes + 4096));
    public (string Key, ImageFrame Frame) Decode(byte[] bytes)
    {
        var key = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var frame = Get<ImageFrame>(("decode", key));
        if (frame is null)
        {
            var decoded = ImagingService.Decode(bytes); frame = new(decoded.pixels, decoded.width, decoded.height);
            Put(("decode", key), frame, frame.Pixels.LongLength);
        }
        return (key, frame);
    }
    public byte[] Thumbnail(ImageFrame frame, string revision, ImageGeometry geometry, bool trim, int alpha, int size)
    {
        var key = new PreviewKey(new(revision, geometry, trim, alpha), size); var old = Get<byte[]>(key); if (old is not null) return old;
        var bytes = ImagingService.Thumbnail(frame.Pixels, frame.Width, frame.Height, size); Put(key, bytes, bytes.LongLength); return bytes;
    }
    public ImageFrame Transform(byte[] pixels, int width, int height, string revision, ImageGeometry geometry, bool trim, int alpha, CancellationToken token = default)
    {
        var key = new TransformKey(revision, geometry, trim, alpha); token.ThrowIfCancellationRequested();
        var existing = Get<ImageFrame>(key); if (existing is not null) return existing;
        var transformed = geometry == new ImageGeometry() ? (pixels, width, height) : ImageTransforms.Apply(pixels, width, height, geometry, token);
        var frame = trim ? ImageQualityConverter.Trim(transformed.Item1, transformed.Item2, transformed.Item3, alpha, token) : new ImageFrame(transformed.Item1, transformed.Item2, transformed.Item3);
        token.ThrowIfCancellationRequested();
        // The decoded source is already retained by the page; avoid counting or caching the same array twice.
        if (!ReferenceEquals(frame.Pixels, pixels)) Put(key, frame, frame.Pixels.LongLength);
        return frame;
    }
    public AsciiDocument Convert(byte[] pixels, int width, int height, string revision, ImageGeometry geometry, ConversionOptions options, string family, CancellationToken token)
    {
        ImageQualityConverter.Validate(options); token.ThrowIfCancellationRequested();
        var transformKey = new TransformKey(revision, geometry, options.TrimTransparent, options.AlphaThreshold);
        var key = new ResultKey(transformKey, options, family); var existing = Get<AsciiDocument>(key); if (existing is not null) return existing;
        var frame = Transform(pixels, width, height, revision, geometry, options.TrimTransparent, options.AlphaThreshold, token);
        AsciiDocument document;
        if (options.Style == ImageArtStyle.Density && !options.MeasureGlyphDensity && options.AdaptiveStrength == 0
            && !options.PreserveTransparent && !options.TrimTransparent && options.PaletteMode == ImagePaletteMode.Original)
            document = AsciiStudio.Core.ImageConverter.Convert(frame.Pixels, frame.Width, frame.Height, options, token);
        else
        {
            var sampleKey = new SampleKey(transformKey, options.Columns, options.Rows, options.CellAspect,
                options.Style == ImageArtStyle.Braille ? 2 : 1, options.Style == ImageArtStyle.Braille ? 4 : options.Style == ImageArtStyle.HalfBlock ? 2 : 1);
            var sample = Get<ImageSample>(sampleKey);
            if (sample is null) { sample = ImageQualityConverter.Sample(frame.Pixels, frame.Width, frame.Height, options, token); token.ThrowIfCancellationRequested(); Put(sampleKey, sample, sample.Rgba.LongLength * sizeof(float)); }
            var filterKey = new FilterKey(sampleKey, options.Brightness, options.Contrast, options.Gamma, options.Saturation, options.Hue, options.Grayscale,
                options.Sepia, options.Sharpness, options.Invert, options.Edges, options.AdaptiveStrength, options.Background, options.PreserveTransparent, options.AlphaThreshold);
            var filtered = Get<ImageFiltered>(filterKey);
            if (filtered is null) { filtered = ImageQualityConverter.Filter(sample, options, token); token.ThrowIfCancellationRequested(); Put(filterKey, filtered, filtered.Density.LongLength * 8); }
            var profile = options.MeasureGlyphDensity && options.Style == ImageArtStyle.Density ? Measure(family, options.Characters, token) : null;
            document = ImageQualityConverter.Map(filtered, options, profile, token);
        }
        token.ThrowIfCancellationRequested();
        Put(key, document, document.Text.Length * 2L + (document.Colors?.LongLength ?? 0) * 4 + (document.BackgroundColors?.LongLength ?? 0) * 4 + 512);
        return document;
    }
    private static GlyphCoverage[] Measure(string family, string characters, CancellationToken token)
    {
        var key = (family, characters); var cached = cache.Get<GlyphCoverage[]>("glyphs", key); if (cached is not null) return cached;
        using var font = new Font(family, 32, FontStyle.Regular, GraphicsUnit.Pixel);
        using var canvas = new Bitmap(1, 1); using var measurement = Graphics.FromImage(canvas);
        var width = Math.Clamp((int)Math.Ceiling(measurement.MeasureString("MMMMMMMM", font, PointF.Empty, StringFormat.GenericTypographic).Width / 8), 1, 256);
        var height = Math.Clamp((int)Math.Ceiling(font.GetHeight(measurement)), 1, 256);
        using var bitmap = new Bitmap(width, height); using var graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var output = new List<GlyphCoverage>();
        foreach (var character in characters.Distinct())
        {
            token.ThrowIfCancellationRequested(); graphics.Clear(Color.White);
            graphics.DrawString(character.ToString(), font, Brushes.Black, PointF.Empty, StringFormat.GenericTypographic);
            double ink = 0;
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) ink += 1 - bitmap.GetPixel(x, y).R / 255d;
            output.Add(new(character, ink / (width * height)));
        }
        var result = output.ToArray(); cache.Put("glyphs", key, result, result.Length * 32L + characters.Length * 2L + family.Length * 2L); return result;
    }
}
