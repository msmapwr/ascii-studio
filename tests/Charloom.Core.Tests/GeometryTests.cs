using Charloom.Core;
using Xunit;

namespace Charloom.Core.Tests;

public sealed class GeometryTests
{
    private readonly byte[] pixels = [0, 0, 0, 255, 255, 255, 255, 255, 255, 0, 0, 255, 0, 0, 255, 0];
    private readonly byte[] transformPixels = Enumerable.Range(0, 6).SelectMany(i => new byte[] { (byte)i, (byte)(20 + i), (byte)(40 + i), (byte)(200 + i) }).ToArray();
    private readonly int[][] rotations = [[0, 1, 2, 3, 4, 5], [4, 2, 0, 5, 3, 1], [5, 4, 3, 2, 1, 0], [1, 3, 5, 0, 2, 4]];
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, false, false)]
    [InlineData(2, false, true)]
    [InlineData(2, true, false)]
    [InlineData(2, true, true)]
    [InlineData(3, false, false)]
    [InlineData(3, false, true)]
    [InlineData(3, true, false)]
    [InlineData(3, true, true)]
    public void RotationAndFlips(int angle, bool horizontal, bool vertical)
    {
        var output = ImageTransforms.Apply(transformPixels, 2, 3, new(QuarterTurns: angle, FlipHorizontal: horizontal, FlipVertical: vertical));
        var expectedWidth = angle % 2 == 0 ? 2 : 3; var expectedHeight = angle % 2 == 0 ? 3 : 2;
        Assert.True(output.Width == expectedWidth && output.Height == expectedHeight, "Rotation dimensions incorrect");
        for (var y = 0; y < expectedHeight; y++) for (var x = 0; x < expectedWidth; x++)
        {
            var row = vertical ? expectedHeight - 1 - y : y;
            var column = horizontal ? expectedWidth - 1 - x : x;
            var label = rotations[angle][row * expectedWidth + column];
            Assert.True(output.Pixels.AsSpan((y * expectedWidth + x) * 4, 4).SequenceEqual(transformPixels.AsSpan(label * 4, 4)), "Pixel color/alpha or orientation changed");
        }
    }

    [Fact(DisplayName = "crop before rotation")]
    public void CropBeforeRotation()
    {
        var output = ImageTransforms.Apply(transformPixels, 2, 3, new(Left: 50, Width: 50, QuarterTurns: 1));
        Assert.True(output.Width == 3 && output.Height == 1, "Crop dimensions incorrect");
        Assert.True(new[] { output.Pixels[0], output.Pixels[4], output.Pixels[8] }.SequenceEqual(new byte[] { 5, 3, 1 }), "Wrong crop rotation order");
    }

    [Fact(DisplayName = "fractional tiny crop remains at least one pixel")]
    public void FractionalTinyCropRemainsAtLeastOnePixel()
    {
        var output = ImageTransforms.Apply(transformPixels, 2, 3, new(Left: 99.9, Top: 99.9, Width: .1, Height: .1));
        Assert.True(output.Width == 1 && output.Height == 1 && output.Pixels[0] == 5, "Tiny crop lost its last pixel");
    }

    [Fact(DisplayName = "invalid geometry rejected")]
    public void InvalidGeometryRejected()
    {
        foreach (var geometry in new ImageGeometry[] { new(Left: -1), new(Width: 0), new(Left: 20), new(Height: double.NaN), new(QuarterTurns: 4) })
        {
            try { ImageTransforms.Apply(transformPixels, 2, 3, geometry); throw new Exception("Invalid crop accepted"); } catch (ArgumentException) { }
        }
        try { ImageTransforms.Apply([0], 2, 3, new()); throw new Exception("Invalid pixels accepted"); } catch (ArgumentException) { }
    }

    [Fact(DisplayName = "transform cancellation and original pixels")]
    public void TransformCancellationAndOriginalPixels()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { ImageTransforms.Apply(transformPixels, 2, 3, new(), cancellation.Token); throw new Exception("Canceled transform succeeded"); } catch (OperationCanceledException) { }
        var output = ImageTransforms.Apply(transformPixels, 2, 3, new()); output.Pixels[0] = 255;
        Assert.True(transformPixels[0] == 0, "Transform overwrote the source");
    }

    [Fact(DisplayName = "geometry undo redo and divergent edits")]
    public void GeometryUndoRedoAndDivergentEdits()
    {
        var history = new ImageGeometryHistory(); history.Apply(new(QuarterTurns: 1)); history.Apply(new(FlipHorizontal: true));
        Assert.True(history.Undo().QuarterTurns == 1 && history.CanRedo, "Undo failed");
        Assert.True(history.Redo().FlipHorizontal, "Redo failed"); history.Undo(); history.Apply(new(QuarterTurns: 2));
        Assert.True(!history.CanRedo && history.Current.QuarterTurns == 2, "New edit retained invalid redo");
        history.Apply(new()); Assert.True(history.Undo().QuarterTurns == 2, "Reset cannot be undone");
        history.Clear(); Assert.True(!history.CanUndo && !history.CanRedo && history.Current == new ImageGeometry(), "Import did not reset history");
    }

    [Fact(DisplayName = "geometry history is bounded and unchanged edits are ignored")]
    public void GeometryHistoryIsBoundedAndUnchangedEditsAreIgnored()
    {
        var history = new ImageGeometryHistory(); history.Apply(new()); Assert.True(!history.CanUndo, "Unchanged state added to history");
        for (var i = 1; i <= 60; i++) history.Apply(new(QuarterTurns: i % 4));
        var count = 0; while (history.CanUndo) { history.Undo(); count++; }
        Assert.True(count == 40, "History exceeded its memory bound");
    }

    [Fact(DisplayName = "geometry JSON round trip")]
    public void GeometryJSONRoundTrip()
    {
        var geometry = new ImageGeometry(10, 20, 50, 60, 3, true, false);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<ImageGeometry>(System.Text.Json.JsonSerializer.Serialize(geometry));
        Assert.True(loaded == geometry, "Saved geometry changed");
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<ImageGeometry>("{}") == new ImageGeometry(), "Missing fields broke defaults");
    }
}
