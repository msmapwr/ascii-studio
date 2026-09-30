using System.Text.Json;
using AsciiStudio.Core;

namespace AsciiStudio.Services;

public sealed record StudioProject(int Version, AsciiDocument Document, ConversionOptions? Options, string? SourceImage, string? SourceText, string Mode, Dictionary<string,string>? Parameters = null);
public sealed record StudioSettings(string Theme = "Dark", double PreviewFontSize = 13, string[]? RecentFiles = null);

public static class WorkspaceService
{
    public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AsciiStudio");
    public static StudioSettings Settings { get; private set; } = LoadSettings();

    public static async Task AtomicWrite(string path, byte[] data)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try { await File.WriteAllBytesAsync(temp, data); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static async Task SaveProject(string path, StudioProject project)
    {
        if(project.Document is null)throw new InvalidDataException("项目缺少字符画。");
        project.Document.Validate();
        var bytes=await Task.Run(()=>JsonSerializer.SerializeToUtf8Bytes(project));
        if(bytes.Length>100_000_000)throw new InvalidDataException("项目超过 100MB，请降低字符画尺寸或输入图片大小。");
        await AtomicWrite(path,bytes);
        await SetSettings(Settings with { RecentFiles = new[] { path }.Concat(Settings.RecentFiles ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToArray() });
    }

    public static async Task<StudioProject> OpenProject(string path)
    {
        if (new FileInfo(path).Length > 100_000_000) throw new InvalidDataException("项目超过 100MB 限制。");
        var project = JsonSerializer.Deserialize<StudioProject>(await File.ReadAllBytesAsync(path)) ?? throw new InvalidDataException("项目为空。");
        if (project.Version != 1) throw new InvalidDataException("不支持此项目版本。");
        if(project.Document is null)throw new InvalidDataException("项目缺少字符画。");
        project.Document.Validate();
        await SetSettings(Settings with { RecentFiles = new[] { path }.Concat(Settings.RecentFiles ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToArray() });
        return project;
    }

    public static async Task SetSettings(StudioSettings settings)
    {
        await AtomicWrite(Path.Combine(DataDirectory, "settings.json"), JsonSerializer.SerializeToUtf8Bytes(settings)); Settings = settings;
    }

    private static StudioSettings LoadSettings()
    {
        try { return JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(Path.Combine(DataDirectory, "settings.json"))) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
}
