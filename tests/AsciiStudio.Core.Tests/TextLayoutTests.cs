using AsciiStudio.Core;
using Xunit;

namespace AsciiStudio.Core.Tests;

public sealed class TextLayoutTests
{
    [Fact(DisplayName = "text layout aligns by Unicode display width")]
    public void TextLayoutAlignsByUnicodeDisplayWidth()
    {
        var art = TextArtLayout.Render("测\na", s => s, new() { Alignment = ArtAlignment.Right, MaximumWidth = 4, Trim = false, Vertical = ArtPacking.Full });
        Assert.True(art == "  测\n   a", "Alignment lost display columns");
    }

    [Fact(DisplayName = "text wrap prefers words and splits long words")]
    public void TextWrapPrefersWordsAndSplitsLongWords()
    {
        var art = TextArtLayout.Render("hi abcde", s => s, new() { Wrap = true, MaximumWidth = 4, Vertical = ArtPacking.Full });
        Assert.True(art == "hi  \nabcd\ne   ", "Word wrapping is incorrect");
    }

    [Fact(DisplayName = "text wrap preserves grapheme clusters")]
    public void TextWrapPreservesGraphemeClusters()
    {
        var art = TextArtLayout.Render("测试😀e\u0301", s => s, new() { Wrap = true, MaximumWidth = 2 });
        Assert.True(art == "测\n试\n😀\ne\u0301 ", "Wrapping split a cluster");
    }

    [Fact(DisplayName = "text spacing preserves blank input lines")]
    public void TextSpacingPreservesBlankInputLines()
    {
        var art = TextArtLayout.Render("a\n\nb", s => s, new() { LineSpacing = 1 });
        Assert.True(art.Split('\n').Length == 5, "Line spacing lost intentional line break");
    }

    [Fact(DisplayName = "vertical kerning and smushing respect collisions")]
    public void VerticalKerningAndSmushingRespectCollisions()
    {
        Assert.True(TextArtLayout.Render("a\nb", s => " x \n   ", new() { Trim = false, Vertical = ArtPacking.Kern }).Split('\n').Length == 3, "Kern overlap failed");
        Assert.True(TextArtLayout.Render("a\nb", s => "x", new() { Vertical = ArtPacking.Smush }) == "x", "Equal-character smush failed");
        Assert.True(TextArtLayout.Render("a\nb", s => s, new() { Vertical = ArtPacking.Kern }) == "a\nb", "Kern overwrote content");
    }

    [Fact(DisplayName = "text frame independent padding and wide title")]
    public void TextFrameIndependentPaddingAndWideTitle()
    {
        var art = TextArtLayout.Frame("a\n测", new() { Border = 2, PaddingX = 2, PaddingY = 1, Title = "测试" }); var lines = art.Split('\n');
        Assert.True(lines.Length == 6 && lines.All(l => UnicodeGrid.Width(l) == 8), "Frame dimensions are inconsistent");
        Assert.True(lines[0].Contains("测试"), "Title missing");
    }

    [Fact(DisplayName = "text layout rejects overflow invalid settings and cancels")]
    public void TextLayoutRejectsOverflowInvalidSettingsAndCancels()
    {
        try { TextArtLayout.Render("too wide", s => s, new() { MaximumWidth = 2 }); throw new Exception("Oversize accepted"); } catch (ArgumentException) { }
        try { new TextArtOptions { Replacement = "测" }.Validate(); throw new Exception("Wide replacement accepted"); } catch (ArgumentException) { }
        try { new TextArtOptions { Wrap = true }.Validate(); throw new Exception("Wrap without width accepted"); } catch (ArgumentException) { }
        using var cts = new CancellationTokenSource(); cts.Cancel(); try { TextArtLayout.Render("x", s => s, new(), 0, cts.Token); throw new Exception("Cancel ignored"); } catch (OperationCanceledException) { }
    }
}
