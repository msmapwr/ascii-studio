using System.Text.Json;
using AsciiStudio.Core;

namespace AsciiStudio.Services;

public static class ProjectFileService
{
    public const int MaximumBytes = 100_000_000;
    public static StudioProject Parse(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("项目超过 100MB 限制。");
        var project = JsonSerializer.Deserialize<StudioProject>(BoundedFile.JsonBytes(bytes).Span) ?? throw new InvalidDataException("项目为空。");
        Validate(project);
        return project with
        {
            Version = WorkspaceService.CurrentProjectVersion,
            Document = UnicodeGrid.Upgrade(project.Document),
            GeneratedDocument = project.GeneratedDocument is null ? null : UnicodeGrid.Upgrade(project.GeneratedDocument)
        };
    }

    public static void Validate(StudioProject project)
    {
        if (project.Version is < 1 or > WorkspaceService.CurrentProjectVersion) throw new InvalidDataException("不支持此项目版本。");
        if (project.Document is null) throw new InvalidDataException("项目缺少字符画。");
        if (project.Mode is not ("image" or "text" or "ansi" or "generator" or "snapshot")) throw new InvalidDataException("不支持此项目类型。");
        project.Document.Validate(); project.GeneratedDocument?.Validate(); project.Geometry?.Validate();
        if (project.Options is not null) ImageQualityConverter.Validate(project.Options);
        if (project.Parameters is { } parameters && (parameters.Count > 128 || parameters.Any(p => p.Key.Length > 128 || p.Value is null)))
            throw new InvalidDataException("项目参数无效。");
        if (project.SourceImage?.Length > (40_000_000L + 2) / 3 * 4) throw new InvalidDataException("项目中的图片超过 40MB。");
        if (project.Mode == "text") _ = TextProjectMapper.Restore(project);
        if (project.Mode == "ansi") _ = AnsiProjectMapper.Restore(project);
    }

    public static async Task<StudioProject> Read(string path, CancellationToken token = default)
    {
        var bytes = await BoundedFile.ReadAsync(path, MaximumBytes, token);
        return await Task.Run(() => { token.ThrowIfCancellationRequested(); return Parse(bytes); }, token);
    }
}
