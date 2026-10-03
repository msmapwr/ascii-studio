using System.Text.Json;
using AsciiStudio.Core;

namespace AsciiStudio.Services;

public sealed record TextCreationRequest(string Text, int Mode, string Font, TextArtOptions Layout,
    TextRasterOptions Raster, string Family, double CellWidth, double CellHeight,
    Dictionary<string, string> Parameters);

/// <summary>Generation, sample rendering and generated-project state, independent of the page.</summary>
public sealed class TextCreationController
{
    public LatestOperation Operations { get; } = new();
    public LatestOperation Previews { get; } = new();
    public string? GeneratedSource { get; private set; }
    public Dictionary<string, string>? GeneratedParameters { get; private set; }
    public int GeneratedColumns { get; private set; } = 120;

    public Task<string[]> Missing(TextCreationRequest r, CancellationToken token) =>
        Task.Run(() => TextRasterService.Missing(r.Text, r.Raster.Family, r.Raster.Bold), token);

    public Task<AsciiDocument> Generate(TextCreationRequest r, CancellationToken token) => Task.Run(() =>
    {
        r.Layout.Validate();
        var text = r.Mode == 0 ? TextFontLibrary.Render(r.Font, r.Text, r.Layout, token)
            : TextRasterService.Render(r.Text, r.Layout, r.Raster, r.CellWidth / r.CellHeight, token);
        token.ThrowIfCancellationRequested();
        var title = string.Concat(TextArtLayout.Elements(r.Text.Split('\n')[0]).Take(32));
        return AsciiDocument.FromText(text, title) with { FontFamily = r.Family, CellWidth = r.CellWidth, CellHeight = r.CellHeight };
    }, token);

    public void Accept(TextCreationRequest r)
    {
        GeneratedSource = r.Text;
        GeneratedColumns = r.Raster.Columns;
        GeneratedParameters = new(r.Parameters);
    }
    public void Restore(StudioProject project)
    {
        Operations.Cancel(); Previews.Cancel();
        GeneratedSource = project.SourceText;
        GeneratedColumns = project.Options?.Columns ?? 120;
        GeneratedParameters = project.Parameters is null ? null : new(project.Parameters);
    }
    public StudioProject Project(AsciiDocument doc) => TextProjectMapper.Project(doc,
        GeneratedColumns, GeneratedSource, GeneratedParameters);

    public async Task<string?> Preview(bool chinese, string font, TextRasterOptions raster, CancellationToken token)
    {
        await Task.Delay(100, token);
        if (!chinese && !TextFontLibrary.Available(font)) return null;
        return await Task.Run(() => chinese ? TextRasterService.Render("测试", new(), raster with { Columns = 48 }, .5, token)
            : TextFontLibrary.Render(font, "abc", new(), token), token);
    }
}

public static class TextProjectMapper
{
    public sealed record State(int Mode, string Font, int Style, TextArtOptions Layout, TextRasterOptions Raster, int Preset, bool AllowMissing);
    public static State Restore(StudioProject project)
    {
        var p = project.Parameters ?? new();
        var mode = p.GetValueOrDefault("mode") == "1" ? 1 : 0;
        var style = int.TryParse(p.GetValueOrDefault("systemStyle"), out var oldStyle) && oldStyle is >= 0 and < 5 ? oldStyle : 0;
        var raster = p.TryGetValue("raster", out var json) ? JsonSerializer.Deserialize<TextRasterOptions>(json) ?? throw new ArgumentException("项目文字栅格参数为空。")
            : new(p.GetValueOrDefault("systemFont", "Microsoft YaHei UI"), Math.Clamp(project.Options?.Columns ?? 120, 16, 600), style, style != 1);
        TextRasterService.ValidateOptions(raster);
        var preset = int.TryParse(p.GetValueOrDefault("readabilityPreset"), out var value) && value is >= 0 and <= 3 ? value : 0;
        return new(mode, p.GetValueOrDefault("font", "Standard"), style, Layout(p), raster, preset, p.GetValueOrDefault("allowMissing") == "True");
    }
    public static StudioProject Project(AsciiDocument doc, int columns, string? source,
        Dictionary<string, string>? parameters) => new(WorkspaceService.CurrentProjectVersion, doc,
        new ConversionOptions { Columns = columns }, null, source, "text", parameters is null ? null : new(parameters));

    public static TextArtOptions Layout(Dictionary<string, string> p)
    {
        var result = p.TryGetValue("layout", out var json) ? JsonSerializer.Deserialize<TextArtOptions>(json) ?? new()
            : new()
            {
                Border = int.TryParse(p.GetValueOrDefault("border"), out var border) && border is >= 0 and <= 3 ? border : 0,
                Trim = p.GetValueOrDefault("trim") != "False",
                Replacement = p.GetValueOrDefault("replacement", "")
            };
        result.Validate();
        return result;
    }
}
