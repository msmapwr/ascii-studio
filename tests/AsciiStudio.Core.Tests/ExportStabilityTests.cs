using System.Xml.Linq;
using AsciiStudio.Core;
using Xunit;

namespace AsciiStudio.Core.Tests;

public sealed class ExportStabilityTests
{
    [Theory]
    [InlineData("AB")]
    [InlineData("A中🙂B")]
    public void HtmlPlacesAsciiAndUnicodeOnExplicitCells(string text)
    {
        var doc = AsciiDocument.FromText(text) with { CellWidth = 7.5, CellHeight = 15.5 };
        var html = ExportService.Html(doc);
        foreach (var glyph in UnicodeGrid.Glyphs(text)) Assert.Contains($"width:{(glyph.Width * 7.5).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}px", html);
        Assert.Equal(UnicodeGrid.Glyphs(text).Count(), html.Split("<span ").Length - 1);
        Assert.Contains("line-height:15.5px", html);
    }
    [Fact]
    public void SvgAlignsPlainAsciiRunWithSavedMetrics()
    {
        var xml = XDocument.Parse(ExportService.Svg(AsciiDocument.FromText("AB") with { CellWidth = 7.5 }));
        var text = xml.Descendants().Single(e => e.Name.LocalName == "text");
        Assert.Equal("15", (string?)text.Attribute("textLength")); Assert.Equal("spacingAndGlyphs", (string?)text.Attribute("lengthAdjust"));
    }
    [Fact]
    public void SvgRejectsXmlForbiddenControlCharacters()
        => Assert.Throws<System.Xml.XmlException>(() => ExportService.Svg(AsciiDocument.FromText("A\0B")));
    [Fact]
    public void HtmlEscapesContentAndPreservesHalfBlockTransparency()
    {
        var doc = AsciiDocument.FromText("▀<&") with { Colors = [0x80FF0000, 0xFFFFFFFF, 0xFFFFFFFF], BackgroundColors = [0xFF00FF00, 0xFF000000, 0xFF000000] };
        var html = ExportService.Html(doc);
        Assert.Contains("#FF000080", html); Assert.Contains("linear-gradient", html); Assert.Contains("&lt;", html); Assert.Contains("&amp;", html);
        Assert.Contains("fill-opacity=\"0.502\"", ExportService.Svg(doc));
    }
    [Fact]
    public void MarkupBudgetStopsLargeExports()
        => Assert.Throws<ArgumentException>(() => ExportService.Html(AsciiDocument.FromText(new string('A', 300_000))));
}
