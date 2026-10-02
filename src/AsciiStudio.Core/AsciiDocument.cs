namespace AsciiStudio.Core;

public sealed record AsciiDocument
{
    /// <summary>0 is the legacy UTF-16 grid; 1 uses Unicode display columns.</summary>
    public int GridVersion { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Text { get; init; } = "";
    public uint[]? Colors { get; init; }
    public uint[]? BackgroundColors { get; init; }
    public string Title { get; init; } = "Untitled";
    public string FontFamily { get; init; } = "Consolas";
    public double CellWidth { get; init; } = 9;
    public double CellHeight { get; init; } = 18;

    public void Validate()
    {
        if (GridVersion is < 0 or > 1) throw new ArgumentException("不支持的字符网格版本。");
        if (string.IsNullOrWhiteSpace(FontFamily) || FontFamily.Length > 128 || FontFamily.Any(char.IsControl)) throw new ArgumentException("字体名称无效。");
        if (!double.IsFinite(CellWidth) || !double.IsFinite(CellHeight) || CellWidth is <= 0 or > 200 || CellHeight is <= 0 or > 200) throw new ArgumentException("字符尺寸无效。");
        if (Width < 0 || Height < 0 || (Height == 0 && Width != 0) || (long)Width * Height > 4_000_000)
            throw new ArgumentException("字符画尺寸超出安全范围。");
        if (Colors is not null && Colors.Length != Width * Height)
            throw new ArgumentException("颜色网格与字符画尺寸不一致。");
        if (BackgroundColors is not null && BackgroundColors.Length != Width * Height)
            throw new ArgumentException("背景颜色网格与字符画尺寸不一致。");
        if (Text is null || Text.Contains('\r')) throw new ArgumentException("字符画必须使用 LF 换行。");
        if (Text.Length > 8_000_000) throw new ArgumentException("字符画文本超过安全范围。");
        try { new System.Text.UTF8Encoding(false, true).GetByteCount(Text); }
        catch (System.Text.EncoderFallbackException) { throw new ArgumentException("字符画包含不完整的 Unicode 字符。"); }
        var lines = Text.Split('\n');
        if ((Text.Length > 0 || Height > 0) && (lines.Length != Height || lines.Any(x => (GridVersion == 0 ? x.Length : UnicodeGrid.Width(x)) > Width)))
            throw new ArgumentException("字符画文本与网格尺寸不一致。");
    }

    public static AsciiDocument FromText(string text, string title = "Untitled")
    {
        text = UnicodeGrid.ExpandTabs(text);
        var lines = text.Split('\n');
        var document = new AsciiDocument { GridVersion = 1, Text = text, Width = lines.Max(UnicodeGrid.Width), Height = lines.Length, Title = title };
        document.Validate();
        return document;
    }
}

public enum DitherMode { None, FloydSteinberg, JarvisJudiceNinke, Stucki, Atkinson }

public sealed record ConversionOptions
{
    public int Columns { get; init; } = 120;
    /// <summary>Zero derives rows from source aspect ratio and CellAspect.</summary>
    public int Rows { get; init; }
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
