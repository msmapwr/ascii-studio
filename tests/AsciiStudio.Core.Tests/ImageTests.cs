using AsciiStudio.Core;
using Xunit;

namespace AsciiStudio.Core.Tests;

public sealed class ImageTests
{
    private readonly byte[] pixels = [0, 0, 0, 255, 255, 255, 255, 255, 255, 0, 0, 255, 0, 0, 255, 0];
    private readonly byte[] transformPixels = Enumerable.Range(0, 6).SelectMany(i => new byte[] { (byte)i, (byte)(20 + i), (byte)(40 + i), (byte)(200 + i) }).ToArray();
    private readonly int[][] rotations = [[0, 1, 2, 3, 4, 5], [4, 2, 0, 5, 3, 1], [5, 4, 3, 2, 1, 0], [1, 3, 5, 0, 2, 4]];
    [Fact(DisplayName = "fixed grid dimensions")]
    public void FixedGridDimensions()
    {
        var d = ImageConverter.Convert(pixels, 2, 2, new() { Columns = 240, Rows = 135 }); d.Validate();
        Assert.True(d.Width == 240 && d.Height == 135, "Wrong fixed grid dimensions");
    }

    [Fact(DisplayName = "automatic aspect correction")]
    public void AutomaticAspectCorrection()
    {
        var d = ImageConverter.Convert(pixels, 2, 2, new() { Columns = 240, CellAspect = .5 }); Assert.True(d.Height == 120, "Wrong automatic rows");
    }

    [Fact(DisplayName = "full HD character grid")]
    public void FullHDCharacterGrid()
    {
        var d = ImageConverter.Convert(pixels, 2, 2, new() { Columns = 1920, Rows = 1080 }); d.Validate(); Assert.True(d.Width * d.Height == 2_073_600, "Full HD was truncated");
    }

    [Fact(DisplayName = "grid boundaries rejected")]
    public void GridBoundariesRejected()
    {
        try { ImageConverter.Convert(pixels, 2, 2, new() { Columns = 2001 }); throw new Exception("Oversized columns accepted"); } catch (ArgumentOutOfRangeException) { }
        try { ImageConverter.Convert(pixels, 2, 2, new() { Columns = 120, Rows = 2001 }); throw new Exception("Oversized rows accepted"); } catch (ArgumentOutOfRangeException) { }
    }

    [Fact(DisplayName = "tall auto grid is not silently distorted")]
    public void TallAutoGridIsNotSilentlyDistorted()
    {
        try { ImageConverter.Convert(new byte[4 * 100], 1, 100, new() { Columns = 240 }); throw new Exception("Tall image was silently clamped"); } catch (ArgumentException) { }
    }

    [Fact(DisplayName = "legacy default options retain aspect")]
    public void LegacyDefaultOptionsRetainAspect()
    {
        var d = ImageConverter.Convert(pixels, 2, 2, new()); Assert.True(d.Width == 120 && d.Height == 60, "Legacy defaults changed");
    }

    [Fact(DisplayName = "cancellation interrupts conversion")]
    public void CancellationInterruptsConversion()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        try { ImageConverter.Convert(pixels, 2, 2, new() { Columns = 1920, Rows = 1080 }, cts.Token); throw new Exception("Canceled conversion succeeded"); } catch (OperationCanceledException) { }
    }

    [Fact(DisplayName = "alpha composite and density extremes")]
    public void AlphaCompositeAndDensityExtremes()
    {
        var d = ImageConverter.Convert([0, 0, 0, 0], 1, 1, new() { Columns = 8, Rows = 1, Characters = " @" }); Assert.True(d.Text == new string(' ', 8), "Transparent pixels became dark");
        var black = ImageConverter.Convert([0, 0, 0, 255], 1, 1, new() { Columns = 8, Rows = 1, Characters = " @" }); Assert.True(black.Text == new string('@', 8), "Black pixels lost density");
    }

    [Fact(DisplayName = "measured density follows coverage rather than order")]
    public void MeasuredDensityFollowsCoverageRatherThanOrder()
    {
        var d = ImageConverter.Convert([0, 0, 0, 255], 1, 1, new() { Columns = 8, Rows = 1, Characters = "@ .", MeasureGlyphDensity = true }, glyphs: [new('@', .8), new(' ', 0), new('.', .1)]);
        Assert.True(d.Text == new string('@', 8), "Measured density ignored the actual font coverage");
        try { ImageConverter.Convert(pixels, 2, 2, new() { MeasureGlyphDensity = true }); throw new Exception("Missing coverage accepted"); } catch (ArgumentException) { }
    }

    [Fact(DisplayName = "Braille dot orientation and monochrome half blocks")]
    public void BrailleDotOrientationAndMonochromeHalfBlocks()
    {
        var input = Enumerable.Repeat((byte)255, 16 * 4 * 4).ToArray(); input[0] = input[1] = input[2] = 0;
        var d = ImageConverter.Convert(input, 16, 4, new() { Columns = 8, Rows = 1, Style = ImageArtStyle.Braille });
        d.Validate(); Assert.True(d.Text == "⠁       ", "Braille first dot or empty glyph incorrect");
        for (var dot = 0; dot < 8; dot++)
        {
            var isolated = Enumerable.Repeat((byte)255, 16 * 4 * 4).ToArray(); var offset = ((dot / 2) * 16 + dot % 2) * 4;
            isolated[offset] = isolated[offset + 1] = isolated[offset + 2] = 0;
            int[] positions = [0, 3, 1, 4, 2, 5, 6, 7];
            Assert.True(ImageConverter.Convert(isolated, 16, 4, new() { Columns = 8, Rows = 1, Style = ImageArtStyle.Braille }).Text[0] == (char)(0x2800 + (1 << positions[dot])), "Braille bit position incorrect");
        }
        var half = Enumerable.Repeat((byte)255, 8 * 2 * 4).ToArray(); half[0] = half[1] = half[2] = 0;
        Assert.True(ImageConverter.Convert(half, 8, 2, new() { Columns = 8, Rows = 1, Style = ImageArtStyle.HalfBlock }).Text[0] == '▀', "Top half mapping incorrect");
    }

    [Fact(DisplayName = "half block retains separate colors and alpha")]
    public void HalfBlockRetainsSeparateColorsAndAlpha()
    {
        var input = new byte[8 * 2 * 4]; for (var i = 0; i < 16; i++) { input[i * 4 + (i < 8 ? 0 : 2)] = 255; input[i * 4 + 3] = 255; }
        var d = ImageConverter.Convert(input, 8, 2, new() { Columns = 8, Rows = 1, Style = ImageArtStyle.HalfBlock, Color = true });
        d.Validate(); Assert.True(d.Text == new string('▀', 8) && d.Colors![0] == 0xFFFF0000 && d.BackgroundColors![0] == 0xFF0000FF, "Top/bottom colors lost");
        input[3] = 0; var alpha = ImageConverter.Convert(input, 8, 2, new() { Columns = 8, Rows = 1, Style = ImageArtStyle.HalfBlock, Color = true, PreserveTransparent = true });
        Assert.True(alpha.Text[0] == '▄' && alpha.BackgroundColors![0] == 0 && alpha.Colors![0] == 0xFF0000FF, "Transparent half became solid");
    }

    [Fact(DisplayName = "transparent trim, blank and composition")]
    public void TransparentTrimBlankAndComposition()
    {
        var input = new byte[3 * 3 * 4]; input[(1 * 3 + 1) * 4 + 3] = 128;
        var frame = ImageQualityConverter.Trim(input, 3, 3, 16); Assert.True(frame.Width == 1 && frame.Height == 1 && frame.Pixels[3] == 128, "Trim lost alpha or dimensions");
        var empty = ImageQualityConverter.Trim(new byte[36], 3, 3, 16); Assert.True(empty.Width == 1 && empty.Height == 1, "Transparent trim produced invalid size");
        var blank = ImageConverter.Convert(new byte[4], 1, 1, new() { Columns = 8, Rows = 1, PreserveTransparent = true, Color = true, Dither = DitherMode.Atkinson });
        Assert.True(blank.Text == new string(' ', 8) && blank.Colors!.All(c => c == 0), "Transparent cells received diffusion");
        var composed = ImageConverter.Convert(new byte[4], 1, 1, new() { Columns = 8, Rows = 1, Background = 0xFFFF0000, Color = true });
        Assert.True(composed.Colors![0] == 0xFFFF0000, "Chosen compositing background ignored");
    }

    [Fact(DisplayName = "structure detects horizontal and vertical contours")]
    public void StructureDetectsHorizontalAndVerticalContours()
    {
        var input = new byte[8 * 8 * 4]; for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) { var p = (y * 8 + x) * 4; input[p] = input[p + 1] = input[p + 2] = x < 4 ? (byte)0 : (byte)255; input[p + 3] = 255; }
        var vertical = ImageConverter.Convert(input, 8, 8, new() { Columns = 8, Rows = 8, Style = ImageArtStyle.Structure });
        Assert.True(vertical.Text.Contains('|'), "Vertical contour missing");
        var rotated = ImageTransforms.Apply(input, 8, 8, new(QuarterTurns: 1));
        Assert.True(ImageConverter.Convert(rotated.Pixels, 8, 8, new() { Columns = 8, Rows = 8, Style = ImageArtStyle.Structure }).Text.Contains('-'), "Horizontal contour missing");
    }

    [Fact(DisplayName = "adaptive filtering and mapping preserve cached inputs")]
    public void AdaptiveFilteringAndMappingPreserveCachedInputs()
    {
        var sample = ImageQualityConverter.Sample(pixels, 2, 2, new() { Columns = 8, Rows = 8 }); var original = (float[])sample.Rgba.Clone();
        var filtered = ImageQualityConverter.Filter(sample, new() { AdaptiveStrength = 1 }); var density = (float[])filtered.Density.Clone(); var colors = (uint[])filtered.Colors.Clone();
        foreach (var mode in Enum.GetValues<DitherMode>()) ImageQualityConverter.Map(filtered, new() { Dither = mode, Color = true, PaletteMode = ImagePaletteMode.Ansi16 });
        Assert.True(original.SequenceEqual(sample.Rgba) && density.SequenceEqual(filtered.Density) && colors.SequenceEqual(filtered.Colors), "Cached stage mutated by later operation");
    }

    [Fact(DisplayName = "palette modes, ANSI encoding and transparent export")]
    public void PaletteModesANSIEncodingAndTransparentExport()
    {
        foreach (var mode in Enum.GetValues<ImagePaletteMode>())
        {
            var d = ImageConverter.Convert(pixels, 2, 2, new() { Columns = 8, Rows = 4, Color = true, PaletteMode = mode, PaletteSize = 4 }); d.Validate();
            if (mode == ImagePaletteMode.Ansi16) Assert.True(!ExportService.Ansi(d).Contains(";2;") && d.Colors!.All(c => ImagePalettes.Ansi16.Contains(c)), "ANSI16 exported as true color");
            if (mode == ImagePaletteMode.Ansi256) Assert.True(ExportService.Ansi(d).Contains(";5;"), "ANSI256 palette encoding missing");
            if (mode == ImagePaletteMode.Limited) Assert.True(d.Colors!.Distinct().Count() <= 4, "Color budget exceeded");
        }
        var alpha = AsciiDocument.FromText("▀") with { Colors = [0x80112233], BackgroundColors = [0] };
        Assert.True(ExportService.Html(alpha).Contains("#11223380") && ExportService.Svg(alpha).Contains("fill-opacity=\"0.502\"") && ExportService.Ansi(alpha).Contains("\x1b[49m"), "Export discarded transparency");
        try { ImagePalettes.Parse("#xx0000,#ffffff"); throw new Exception("Invalid palette accepted"); } catch (ArgumentException) { }
    }

    [Fact(DisplayName = "quality bounds and cancellation")]
    public void QualityBoundsAndCancellation()
    {
        foreach (var options in new ConversionOptions[] { new() { Style = (ImageArtStyle)99 }, new() { AdaptiveStrength = double.NaN }, new() { PaletteSize = 65 }, new() { AlphaThreshold = 0 }, new() { Style = ImageArtStyle.Braille, Columns = 2000, Rows = 2000 } })
        { try { ImageConverter.Convert(pixels, 2, 2, options); throw new Exception("Invalid or oversized quality options accepted"); } catch (ArgumentException) { } }
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { ImageConverter.Convert(pixels, 2, 2, new() { Style = ImageArtStyle.Braille }, cancel.Token); throw new Exception("Cancelled quality conversion accepted"); } catch (OperationCanceledException) { }
    }

    [Fact(DisplayName = "cache owner/global budget and LRU release")]
    public void CacheOwnerGlobalBudgetAndLRURelease()
    {
        var cache = new BudgetCache(400, 650); cache.Put("a", 1, "one", 100); cache.Put("a", 2, "two", 100);
        Assert.True(cache.Get<string>("a", 1) == null && cache.OwnerBytes("a") <= 400, "Owner budget exceeded");
        cache.Put("b", 1, "three", 100); cache.Put("b", 2, "four", 100);
        cache.Put("c", 1, "five", 100);
        Assert.True(cache.RetainedBytes <= 650 && cache.Get<string>("a", 2) == null, "Global budget or LRU failed");
        cache.RemoveOwner("b"); cache.RemoveOwner("c"); Assert.True(cache.RetainedBytes == 0, "Owner release retained values");
        cache.Put("a", 3, "oversized", 1000); Assert.True(cache.RetainedBytes == 0, "Oversized entry retained");
    }

    [Fact(DisplayName = "adaptive suppresses flat-region noise")]
    public void AdaptiveSuppressesFlatRegionNoise()
    {
        var rgba = new float[8 * 8 * 4];
        for (var i = 0; i < 64; i++) { rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 128; rgba[i * 4 + 3] = 1; }
        var center = 3 * 8 + 3; rgba[center * 4] = rgba[center * 4 + 1] = rgba[center * 4 + 2] = 140;
        var sample = new ImageSample(rgba, 8, 8, 8, 8, 1, 1);
        var original = ImageQualityConverter.Filter(sample, new()); var adaptive = ImageQualityConverter.Filter(sample, new() { AdaptiveStrength = 1 });
        Assert.True(Math.Abs(adaptive.Density[center] - original.Density[0]) < Math.Abs(original.Density[center] - original.Density[0]), "Flat noise was not reduced");
    }

    [Fact(DisplayName = "block exports fill exact cell fractions")]
    public void BlockExportsFillExactCellFractions()
    {
        var d = AsciiDocument.FromText("▀▄█") with { Colors = [0xFFFF0000, 0xFF00FF00, 0xFF0000FF] };
        var svg = ExportService.Svg(d); var html = ExportService.Html(d);
        Assert.True(svg.Contains("y=\"20\" width=\"9\" height=\"9\"") && svg.Contains("y=\"29\" width=\"9\" height=\"9\"") && svg.Contains("height=\"18\" fill=\"#0000FF\""), "Blocks depend on font baseline or gaps");
        Assert.True(html.Contains("linear-gradient") && html.Contains("▀") && html.Contains("▄"), "HTML did not retain block text and geometric appearance");
    }

    [Fact(DisplayName = "cancellation stops an active quality computation")]
    public void CancellationStopsAnActiveQualityComputation()
    {
        var large = new byte[2048 * 2048 * 4];
        using var cancel = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        // CancelAfter uses the pool shared with parallel CPU tests. A dedicated thread
        // keeps cancellation independent of pool starvation on small CI runners.
        var cancellation = new Thread(() => { started.Wait(); Thread.Sleep(10); cancel.Cancel(); }) { IsBackground = true };
        cancellation.Start();
        try
        {
            Assert.False(cancel.IsCancellationRequested);
            started.Set();
            Assert.ThrowsAny<OperationCanceledException>(() => ImageConverter.Convert(large, 2048, 2048,
                new() { Columns = 1000, Rows = 500, Style = ImageArtStyle.Braille }, cancel.Token));
            Assert.True(cancel.IsCancellationRequested, "Unexpected cancellation token");
        }
        finally { started.Set(); cancellation.Join(); }
    }
}
