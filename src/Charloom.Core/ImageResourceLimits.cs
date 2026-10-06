namespace Charloom.Core;

/// <summary>Shared image budgets; axis limits also require the total pixel/sample budget.</summary>
public static class ImageResourceLimits
{
    public const int FileBytes = 100_000_000;
    public const int SourcePixels = 200_000_000;
    public const int ProcessingSide = 12_000;
    public const int ProcessingPixels = 16_000_000;
    public const int GridSide = 200_000;
    public const int SamplePoints = 40_000_000;
    public const int BitmapPixels = 100_000_000;
    public const int BitmapSide = 32_767;

    public static (int Width, int Height) ProcessingSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > SourcePixels)
            throw new ArgumentException("源图尺寸无效或超过 2 亿像素。");
        var scale = Math.Min(1d, Math.Min(ProcessingSide / (double)Math.Max(width, height),
            Math.Sqrt(ProcessingPixels / ((double)width * height))));
        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }
}
