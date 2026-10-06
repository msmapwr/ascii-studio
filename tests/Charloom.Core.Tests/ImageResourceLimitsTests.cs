using Charloom.Core;
using Xunit;

namespace Charloom.Core.Tests;

public sealed class ImageResourceLimitsTests
{
    [Theory]
    [InlineData(3000, 1, 3000, 1)]
    [InlineData(200000, 1, 12000, 1)]
    [InlineData(10000, 10000, 4000, 4000)]
    [InlineData(20000, 10000, 5656, 2828)]
    public void ProcessingSizePreservesAspectWithinBothBudgets(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), ImageResourceLimits.ProcessingSize(width, height));
        Assert.Throws<ArgumentException>(() => ImageResourceLimits.ProcessingSize(20001, 10000));
    }

    [Theory]
    [InlineData(ImageArtStyle.Density)]
    [InlineData(ImageArtStyle.Structure)]
    [InlineData(ImageArtStyle.HalfBlock)]
    [InlineData(ImageArtStyle.Braille)]
    public void LargerAxisConvertsAndOversizedProductIsRejectedBeforeAllocation(ImageArtStyle style)
    {
        var options = new ConversionOptions { Columns = 200000, Rows = 1, Style = style };
        var result = ImageConverter.Convert([0, 0, 0, 255], 1, 1, options);
        Assert.Equal(200000, result.Width); Assert.Equal(1, result.Height); result.Validate();
        var points = style == ImageArtStyle.Braille ? 8 : style == ImageArtStyle.HalfBlock ? 2 : 1;
        Assert.Throws<ArgumentException>(() => ImageConverter.Convert([0, 0, 0, 255], 1, 1,
            options with { Rows = ImageResourceLimits.SamplePoints / options.Columns / points + 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageConverter.Convert([0, 0, 0, 255], 1, 1,
            options with { Columns = 200001 }));
    }
}
