using AsciiStudio.Core;
using Xunit;

namespace AsciiStudio.Core.Tests;

public sealed class ViewportTests
{
    [Fact(DisplayName = "viewport index bounded pages retain complete text")]
    public void ViewportIndexBoundedPagesRetainCompleteText()
    {
        var text = string.Join('\n', Enumerable.Range(0, 1500).Select(i => $"{i:D4}:" + new string('x', 195)));
        var index = new DocumentViewIndex(AsciiDocument.FromText(text));
        Assert.True(index.IsPaged && index.Pages.Length > 1 && index.RowStarts.Length == 1500, "Large text not indexed");
        Assert.True(string.Concat(index.Pages.Select(p => text.Substring(p.Start, p.Length))) == text, "Page segmentation lost text");
        Assert.True(index.Pages.All(p => p.Length <= 64012) && index.Line(999).StartsWith("0999:"), "Page budget or row index invalid");
        var page = index.Pages[1]; var replacement = "edit\r\n中";
        Assert.True(index.ReplacePage(1, replacement) == text[..page.Start] + "edit\n中" + text[(page.Start + page.Length)..], "Paged edit replaced more than one page");
        Assert.True(index.PageAt(page.Start) == 1 && index.PageAt(text.Length) == index.Pages.Length - 1, "Boundary page lookup failed");
    }

    [Fact(DisplayName = "viewport pages preserve graphemes and positions")]
    public void ViewportPagesPreserveGraphemesAndPositions()
    {
        var cluster = "👨‍👩‍👧‍👦";
        var text = new string('a', 63999) + cluster + new string('b', 150000);
        var index = new DocumentViewIndex(AsciiDocument.FromText(text));
        Assert.True(index.Pages[0].Length == 63999 + cluster.Length && index.Pages[1].Start == index.Pages[0].Length, "Page split an emoji cluster");
        Assert.True(DocumentViewIndex.Prefix(text, 64000).Length == 63999 && DocumentViewIndex.Prefix(cluster, 1) == "", "Prefix split a cluster");
        var small = new DocumentViewIndex(AsciiDocument.FromText("中é😀\nxyz"));
        Assert.True(!small.IsPaged && small.Position(2) == (0, 3) && small.Position(7) == (1, 1), "Unicode selection coordinates wrong");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { new DocumentViewIndex(small.Document, cancelled.Token); throw new Exception("Cancelled index accepted"); } catch (OperationCanceledException) { }
    }

    [Fact(DisplayName = "viewport physical allocation budget at any DPI")]
    public void ViewportPhysicalAllocationBudgetAtAnyDPI()
    {
        foreach (var density in new double[] { 1, 1.25, 1.5, 2, 3, 8 })
        {
            var region = ViewportRegion.Create(12000, 34000, 7680, 4320, density);
            Assert.True(region.PixelWidth <= 4096 && region.PixelHeight <= 4096 && (long)region.PixelWidth * region.PixelHeight <= 4_000_000, "Viewport exceeded raster budget");
            Assert.True(region.X == 11984 && region.Y == 33984, "Viewport forgot scroll origin");
        }
        Assert.True(ViewportRegion.Create(0, 0, 480, 480, 1.25).PixelWidth == 640, "DPI density lost at normal sizes");
        try { ViewportRegion.Create(0, 0, double.NaN, 1, 1); throw new Exception("Invalid region accepted"); } catch (ArgumentException) { }
    }
}
