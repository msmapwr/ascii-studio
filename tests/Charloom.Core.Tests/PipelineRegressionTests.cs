using Charloom.Core;
using Xunit;

namespace Charloom.Core.Tests;

public sealed class PipelineRegressionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RefactoredPipelineMatchesAlpha7(int dither)
    {
        var random = new Random(1701);
        var pixels = new byte[47 * 39 * 4]; random.NextBytes(pixels);
        foreach (var style in Enum.GetValues<ImageArtStyle>())
            foreach (var edges in new[] { false, true })
                foreach (var quality in new[] { false, true })
                    foreach (var palette in Enum.GetValues<ImagePaletteMode>())
                    {
                        var o = new ConversionOptions
                        {
                            Columns = 32,
                            Rows = 17,
                            Dither = (DitherMode)dither,
                            Style = style,
                            Edges = edges,
                            Color = true,
                            PaletteMode = palette,
                            MeasureGlyphDensity = quality && style == ImageArtStyle.Density,
                            Brightness = 1.1,
                            Contrast = .9,
                            Gamma = 1.3,
                            Saturation = .8,
                            Hue = 17,
                            Grayscale = .1,
                            Sepia = .2,
                            Sharpness = .4,
                            AdaptiveStrength = quality ? .3 : 0,
                            PreserveTransparent = quality,
                            TrimTransparent = quality
                        };
                        var profile = o.Characters.Distinct().Select((c, i) => new GlyphCoverage(c, Math.Pow(i / (double)(o.Characters.Distinct().Count() - 1), .8))).ToArray();
                        var expected = LegacyImageConverter.Convert(pixels, 47, 39, o, glyphs: profile);
                        var actual = ImageConverter.Convert(pixels, 47, 39, o, glyphs: profile);
                        Assert.Equal(expected.Text, actual.Text);
                        Assert.Equal(expected.Colors, actual.Colors);
                        Assert.Equal(expected.BackgroundColors, actual.BackgroundColors);
                        Assert.Equal(expected.ColorEncoding, actual.ColorEncoding);
                        Assert.Equal(expected.Width, actual.Width); Assert.Equal(expected.Height, actual.Height);
                    }
    }

    [Fact]
    public void SupersededWorkCannotCommitOrClearNewWork()
    {
        var scheduler = new LatestOperation();
        using var first = scheduler.Begin();
        using var second = scheduler.Begin();
        Assert.True(first.Token.IsCancellationRequested); Assert.False(first.IsCurrent);
        Assert.True(scheduler.HasNewer(first)); first.Dispose(); Assert.True(second.IsCurrent);
        scheduler.Cancel(); Assert.False(second.IsCurrent); Assert.True(second.Token.IsCancellationRequested);
        Assert.False(scheduler.HasNewer(second));
    }

    [Fact]
    public void ColorAndEdgeStagesNeverMutateTheirInputs()
    {
        var o = new ConversionOptions { Columns = 8, Rows = 2, AdaptiveStrength = .5, Edges = true };
        var sample = ImageQualityConverter.Sample([25, 125, 225, 255], 1, 1, o);
        var original = sample.Rgba.ToArray();
        var adjusted = ImageQualityConverter.AdjustColors(sample, o);
        var density = adjusted.Density.ToArray(); var colors = adjusted.Colors.ToArray();
        var filtered = ImageQualityConverter.ApplyEdges(adjusted, o);
        Assert.Equal(original, sample.Rgba); Assert.Equal(density, adjusted.Density);
        Assert.Equal(colors, adjusted.Colors); Assert.NotSame(adjusted.Density, filtered.Density);
    }
}
