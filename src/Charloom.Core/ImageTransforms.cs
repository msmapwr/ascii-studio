namespace Charloom.Core;

/// <summary>Crop coordinates refer to the decoded, EXIF-oriented source, before rotation.</summary>
public sealed record ImageGeometry(double Left = 0, double Top = 0, double Width = 100, double Height = 100,
    int QuarterTurns = 0, bool FlipHorizontal = false, bool FlipVertical = false)
{
    public void Validate()
    {
        if (!new[] { Left, Top, Width, Height }.All(double.IsFinite) || Left < 0 || Top < 0 || Width <= 0 || Height <= 0
            || Left + Width > 100 || Top + Height > 100 || QuarterTurns is < 0 or > 3)
            throw new ArgumentException("裁剪区域必须在原图范围内，宽度和高度必须大于 0。旋转只能为 0、90、180 或 270 度。");
    }
}

public static class ImageTransforms
{
    /// <summary>Crop, rotate clockwise, then flip in output coordinates. Never changes source pixels.</summary>
    public static (byte[] Pixels, int Width, int Height) Apply(byte[] rgba, int width, int height,
        ImageGeometry geometry, CancellationToken cancellationToken = default)
    {
        if (width <= 0 || height <= 0 || (long)width * height > ImageResourceLimits.SourcePixels || (long)width * height * 4 != rgba.LongLength)
            throw new ArgumentException("图片像素数据或尺寸无效。");
        geometry.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var left = Math.Min(width - 1, (int)Math.Floor(width * geometry.Left / 100));
        var top = Math.Min(height - 1, (int)Math.Floor(height * geometry.Top / 100));
        var right = Math.Clamp((int)Math.Ceiling(width * (geometry.Left + geometry.Width) / 100), left + 1, width);
        var bottom = Math.Clamp((int)Math.Ceiling(height * (geometry.Top + geometry.Height) / 100), top + 1, height);
        var cropWidth = right - left; var cropHeight = bottom - top;
        var rotated = geometry.QuarterTurns % 2 != 0;
        var outputWidth = rotated ? cropHeight : cropWidth;
        var outputHeight = rotated ? cropWidth : cropHeight;
        var output = new byte[checked(outputWidth * outputHeight * 4)];
        for (var y = 0; y < cropHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < cropWidth; x++)
            {
                var (dx, dy) = geometry.QuarterTurns switch
                {
                    1 => (cropHeight - 1 - y, x),
                    2 => (cropWidth - 1 - x, cropHeight - 1 - y),
                    3 => (y, cropWidth - 1 - x),
                    _ => (x, y)
                };
                if (geometry.FlipHorizontal) dx = outputWidth - 1 - dx;
                if (geometry.FlipVertical) dy = outputHeight - 1 - dy;
                rgba.AsSpan(((top + y) * width + left + x) * 4, 4).CopyTo(output.AsSpan((dy * outputWidth + dx) * 4, 4));
            }
        }
        return (output, outputWidth, outputHeight);
    }
}

/// <summary>Bounded history stores parameters, not full image buffers.</summary>
public sealed class ImageGeometryHistory
{
    private readonly List<ImageGeometry> states = [new()];
    private int index;
    public ImageGeometry Current => states[index];
    public bool CanUndo => index > 0;
    public bool CanRedo => index < states.Count - 1;
    public void Apply(ImageGeometry geometry)
    {
        geometry.Validate();
        if (geometry == Current) return;
        states.RemoveRange(index + 1, states.Count - index - 1);
        states.Add(geometry);
        if (states.Count > 41) states.RemoveAt(0);
        index = states.Count - 1;
    }
    public ImageGeometry Undo() { if (CanUndo) index--; return Current; }
    public ImageGeometry Redo() { if (CanRedo) index++; return Current; }
    public void Clear(ImageGeometry? geometry = null)
    {
        var value = geometry ?? new(); value.Validate();
        states.Clear(); states.Add(value); index = 0;
    }
}
