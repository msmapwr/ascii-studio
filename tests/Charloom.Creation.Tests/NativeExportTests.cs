using System.Drawing;
using Charloom.Core;
using Charloom.Services;
using Xunit;

namespace Charloom.Creation.Tests;

public sealed class NativeExportTests
{
    [Fact]
    public void TransparentHalfBlocksKeepIndependentAlphaAndColors()
    {
        var document = AsciiDocument.FromText("▀") with { Colors = [0x80FF0000], BackgroundColors = [0x400000FF] };
        using var stream = new MemoryStream(ImagingService.Render(document, 32, 0, true));
        using var image = new Bitmap(stream);
        var upper = image.GetPixel(image.Width / 2, image.Height / 4);
        var lower = image.GetPixel(image.Width / 2, image.Height * 3 / 4);
        Assert.Equal(128, upper.A); Assert.Equal(255, upper.R); Assert.Equal(0, upper.B);
        Assert.Equal(64, lower.A); Assert.Equal(255, lower.B); Assert.Equal(0, lower.R);
    }
    [Fact]
    public void ProportionalFontHasSameGridWithOrWithoutExplicitColors()
    {
        var plain = AsciiDocument.FromText("iiiiWW\nWWiiii") with { FontFamily = "Arial" };
        var colored = plain with { Colors = Enumerable.Repeat(0xFFE7EDF7u, plain.Width * plain.Height).ToArray() };
        Assert.Equal(ImagingService.Render(plain, transparent: true), ImagingService.Render(colored, transparent: true));
    }
    [Fact]
    public void UnicodeExportUsesGridColumnsAndKeepsTransparentPadding()
    {
        var document = AsciiDocument.FromText("中🙂A");
        var size = ImagingService.RenderSize(document, padding: 10);
        using var stream = new MemoryStream(ImagingService.Render(document, padding: 10, transparent: true));
        using var image = new Bitmap(stream);
        Assert.Equal(size, (image.Width, image.Height)); Assert.Equal(0, image.GetPixel(0, 0).A);
        Assert.Throws<ArgumentException>(() => ImagingService.RenderSize(document, scale: 5));
        Assert.Throws<ArgumentException>(() => ImagingService.RenderSize(document, size: float.NaN));
    }
}
