using System.Globalization;

namespace AsciiStudio.Core;

/// <summary>Stable source parameters; the saved document remains independently editable.</summary>
public sealed record GeneratorRecipe(int Kind = 0, int Style = 0, string Text = "Hello, ASCII!", int Width = 60, int Height = 20, int Seed = 42)
{
    public void Validate()
    {
        if (Kind is < 0 or > 6 || Style is < 0 or > 3 || Width is < 8 or > 300 || Height is < 3 or > 100 || Seed < 0)
            throw new ArgumentException("生成器参数超出支持范围。");
        if (Kind == 2 && (Width > 100 || Height > 80)) throw new ArgumentException("迷宫尺寸最多为 100 × 80。");
        if (Text is null || Text.Length > 2_000_000) throw new ArgumentException("生成器输入过大。");
        if (Kind == 0)
        {
            var rows = TextUtilities.Normalize(Text).Split('\n');
            if (rows.Length > 1000 || rows.Any(row => row.Length > 2000)) throw new ArgumentException("边框内容最多 1000 行，每行最多 2000 字符。");
        }
    }

    public string Generate()
    {
        Validate();
        return Kind switch
        {
            0 => Generators.Border(Text, Math.Min(Style + 1, 3)),
            1 => Generators.Divider(Width, Style),
            2 => Generators.Maze(Width, Height, Seed),
            _ => Generators.Pattern(Width, Height, Kind - 3, Seed)
        };
    }

    public Dictionary<string, string> ToParameters()
    {
        Validate();
        return new()
        {
            ["schema"] = "1",
            ["kind"] = Kind.ToString(CultureInfo.InvariantCulture),
            ["style"] = Style.ToString(CultureInfo.InvariantCulture),
            ["width"] = Width.ToString(CultureInfo.InvariantCulture),
            ["height"] = Height.ToString(CultureInfo.InvariantCulture),
            ["seed"] = Seed.ToString(CultureInfo.InvariantCulture)
        };
    }

    public static GeneratorRecipe FromParameters(IReadOnlyDictionary<string, string>? parameters, string? text)
    {
        if (parameters is null || !parameters.TryGetValue("schema", out var schema) || schema != "1")
            throw new ArgumentException("不支持此生成器参数版本。");
        int Read(string name) => parameters.TryGetValue(name, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number : throw new ArgumentException($"生成器项目缺少有效参数：{name}。");
        var recipe = new GeneratorRecipe(Read("kind"), Read("style"), TextUtilities.Normalize(text ?? ""), Read("width"), Read("height"), Read("seed"));
        recipe.Validate(); return recipe;
    }
}
