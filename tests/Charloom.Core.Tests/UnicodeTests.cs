using Charloom.Core;
using Xunit;

namespace Charloom.Core.Tests;

public sealed class UnicodeTests
{
    private readonly byte[] pixels = [0, 0, 0, 255, 255, 255, 255, 255, 255, 0, 0, 255, 0, 0, 255, 0];
    [Fact(DisplayName = "Unicode clusters and stable terminal widths")]
    public void UnicodeClustersAndStableTerminalWidths()
    {
        foreach (var (text, expected) in new (string, int)[] { ("ABC", 3), ("测试A", 5), ("e\u0301", 1), ("👨‍👩‍👧‍👦", 2), ("🇨🇳", 2), ("1️⃣", 2), ("中👩🏽‍💻x", 5), ("╔═╗", 3), ("\u0301", 0), ("\U00020000", 2) })
            Assert.True(UnicodeGrid.Width(text) == expected, $"Wrong width for {text}");
        Assert.True(UnicodeGrid.Glyphs("👨‍👩‍👧‍👦").Count() == 1, "Emoji sequence was split");
        Assert.True(AsciiDocument.FromText("中\tx\ne\u0301\tZ").Text == "中  x\ne\u0301   Z", "Tab stops ignored display width");
    }

    [Fact(DisplayName = "Unicode validation and border alignment")]
    public void UnicodeValidationAndBorderAlignment()
    {
        var bordered = Generators.Border("测试\ne\u0301\n👨‍👩‍👧‍👦", 2);
        Assert.True(bordered.Split('\n').Select(UnicodeGrid.Width).Distinct().Count() == 1, "Unicode border was misaligned");
        Assert.True(AsciiDocument.FromText("中e\u0301😀").Width == 5, "Grid uses UTF-16 length");
        try { AsciiDocument.FromText("\ud800"); throw new Exception("Unpaired surrogate accepted"); } catch (ArgumentException) { }
        try { (AsciiDocument.FromText("测试") with { Width = 2 }).Validate(); throw new Exception("Wide text overflow accepted"); } catch (ArgumentException) { }
        try { new AsciiDocument { Width = 10, Height = 0, BackgroundColors = [] }.Validate(); throw new Exception("Zero row color grid accepted"); } catch (ArgumentException) { }
        try { ImageConverter.Convert(pixels, 2, 2, new() { Characters = " 中" }); throw new Exception("Wide image ramp accepted"); } catch (ArgumentException) { }
    }

    [Fact(DisplayName = "Unicode legacy foreground and background migration")]
    public void UnicodeLegacyForegroundAndBackgroundMigration()
    {
        var legacy = new AsciiDocument { Text = "中e\u0301😀!", Width = 6, Height = 1, Colors = [1, 2, 3, 4, 5, 6], BackgroundColors = [11, 12, 13, 14, 15, 16] };
        var upgraded = UnicodeGrid.Upgrade(legacy);
        Assert.True(upgraded.GridVersion == 1 && upgraded.Width == 6, "Wrong migrated grid");
        Assert.True(upgraded.Colors!.SequenceEqual(new uint[] { 1, 1, 2, 4, 4, 6 }) && upgraded.BackgroundColors!.SequenceEqual(new uint[] { 11, 11, 12, 14, 14, 16 }), "Migration split glyph colors");
        Assert.True(ReferenceEquals(UnicodeGrid.Upgrade(upgraded), upgraded), "Migration is not idempotent");
        var grown = UnicodeGrid.Upgrade(new AsciiDocument { Text = "测试", Width = 2, Height = 1, Colors = [1, 2] });
        Assert.True(grown.Width == 4 && grown.Colors!.SequenceEqual(new uint[] { 1, 1, 2, 2 }), "CJK migration did not expand columns");
    }

    [Fact(DisplayName = "Unicode SVG HTML and ANSI keep clusters and colors")]
    public void UnicodeSVGHTMLAndANSIKeepClustersAndColors()
    {
        var document = AsciiDocument.FromText("中e\u0301😀!") with { Colors = [0xFFFF0000, 0xFFFF0000, 0xFF00FF00, 0xFF0000FF, 0xFF0000FF, 0xFFFFFFFF] };
        var svg = ExportService.Svg(document);
        var xml = System.Xml.Linq.XDocument.Parse(svg);
        var nodes = xml.Descendants().Where(n => n.Name.LocalName == "text").ToArray();
        Assert.True(nodes.Length == 4 && nodes[1].Value == "e\u0301" && nodes[2].Value == "😀" && (string?)nodes[3].Attribute("x") == "65", "SVG split glyphs or used UTF-16 positions");
        Assert.True(ExportService.Html(document).Contains("width:18px") && ExportService.Html(document).Contains("e\u0301"), "HTML missing fixed widths");
        var restored = AnsiArt.Parse(ExportService.Ansi(document), 20).Document;
        Assert.True(restored.Text.TrimEnd() == document.Text && restored.Colors!.Take(6).SequenceEqual(document.Colors!), "ANSI Unicode round trip changed glyphs or colors");
    }

    [Fact(DisplayName = "Unicode ANSI wide cursor wrap and overwrite")]
    public void UnicodeANSIWideCursorWrapAndOverwrite()
    {
        var parsed = AnsiArt.Parse(new string('a', 19) + "中Z", 20).Document;
        Assert.True(parsed.Height == 2 && parsed.Text.Split('\n')[1].StartsWith("中Z"), "Wide glyph did not wrap before last column");
        var overwritten = AnsiArt.Parse("中\x1b[2GX", 20).Document;
        Assert.True(overwritten.Text.StartsWith(" X"), "Overwriting continuation left broken wide glyph");
        var combined = AnsiArt.Parse("e\x1b[31m\u0301X", 20).Document;
        Assert.True(combined.Text.StartsWith("e\u0301X"), "Color escape broke combining mark");
    }
}
