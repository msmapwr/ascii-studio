using Charloom.Core;
using Xunit;

namespace Charloom.Core.Tests;

public sealed class DocumentTests
{
    [Fact(DisplayName = "font metrics survive project JSON")]
    public void FontMetricsSurviveProjectJSON()
    {
        var document = AsciiDocument.FromText("abc") with { FontFamily = "Courier New", CellWidth = 7.8, CellHeight = 15.1 };
        var restored = System.Text.Json.JsonSerializer.Deserialize<AsciiDocument>(System.Text.Json.JsonSerializer.Serialize(document))!;
        restored.Validate(); Assert.True(restored == document, "Font metadata changed");
    }

    [Fact(DisplayName = "SVG font metrics use invariant numbers and escaped family")]
    public void SVGFontMetricsUseInvariantNumbersAndEscapedFamily()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("fr-FR");
            var svg = ExportService.Svg(AsciiDocument.FromText("a") with { FontFamily = "A<&", CellWidth = 7.5, CellHeight = 15.5 });
            Assert.True(svg.Contains("width=\"47.5\"") && svg.Contains("A&lt;&amp;"), "SVG metrics or font escaping failed");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
}
