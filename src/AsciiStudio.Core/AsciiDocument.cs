namespace AsciiStudio.Core;

public sealed record AsciiDocument
{
    public int Width { get; init; }
    public int Height { get; init; }
    public string Text { get; init; } = "";
    public uint[]? Colors { get; init; }
    public string Title { get; init; } = "Untitled";

    public void Validate()
    {
        if (Width < 0 || Height < 0 || (long)Width * Height > 2_000_000)
            throw new ArgumentException("字符画尺寸超出安全范围。");
        if (Colors is not null && Colors.Length != Width * Height)
            throw new ArgumentException("颜色网格与字符画尺寸不一致。");
        if(Text is null || Text.Contains('\r'))throw new ArgumentException("字符画必须使用 LF 换行。");
        var lines = Text.Split('\n');
        if (Text.Length > 4_000_000 || (Text.Length > 0 && (lines.Length != Height || lines.Any(x => x.Length > Width))))
            throw new ArgumentException("字符画文本与网格尺寸不一致。");
    }

    public static AsciiDocument FromText(string text, string title = "Untitled")
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        var document = new AsciiDocument { Text = text, Width = lines.Max(x => x.Length), Height = lines.Length, Title = title };
        document.Validate();
        return document;
    }
}

public enum DitherMode { None, FloydSteinberg, JarvisJudiceNinke, Stucki, Atkinson }

public sealed record ConversionOptions
{
    public int Columns { get; init; } = 120;
    public double CellAspect { get; init; } = 0.5;
    public string Characters { get; init; } = " .:-=+*#%@";
    public double Brightness { get; init; } = 1;
    public double Contrast { get; init; } = 1;
    public double Gamma { get; init; } = 1;
    public double Saturation { get; init; } = 1;
    public double Hue { get; init; }
    public double Grayscale { get; init; }
    public double Sepia { get; init; }
    public bool Invert { get; init; }
    public bool Color { get; init; }
    public bool Threshold { get; init; }
    public int ThresholdValue { get; init; } = 128;
    public bool Edges { get; init; }
    public double Sharpness { get; init; }
    public DitherMode Dither { get; init; }
    public uint Background { get; init; } = 0xFFFFFFFF;
}
