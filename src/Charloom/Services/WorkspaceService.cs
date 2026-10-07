using System.Text.Json;
using Charloom.Core;

namespace Charloom.Services;

public sealed record StudioProject(int Version, AsciiDocument Document, ConversionOptions? Options, string? SourceImage, string? SourceText, string Mode, Dictionary<string, string>? Parameters = null, ImageGeometry? Geometry = null, bool Edited = false, AsciiDocument? GeneratedDocument = null);
public sealed record StudioSettings(string Theme = "Dark", double PreviewFontSize = 13, string[]? RecentFiles = null,
    bool Animations = true, bool WordWrap = false, bool ShowStats = true, bool CompactLayout = false,
    string DefaultExportFormat = "TXT", int ExportScale = 1, string FilePrefix = "",
    bool AutoConvert = true, int ConversionDelay = 180, int DefaultColumns = 120, bool RememberWindow = true,
    double PreviewZoom = 1, bool BeginnerMode = false, string UiFontFamily = "Segoe UI", double UiFontSize = 14,
    string[]? FavoriteSettings = null, string UiLanguage = "system");

public static class WorkspaceService
{
    public const int CurrentProjectVersion = 4;
    public static AsciiDocument? CurrentArt { get; set; }
    public static string DataDirectory { get; } = ProductIdentity.EnvironmentValue("CHARLOOM_DATA_DIRECTORY", "ASCIISTUDIO_DATA_DIRECTORY") is { Length: > 0 } directory
        ? Path.GetFullPath(directory) : ProductIdentity.DesktopDataDirectory;
    public static StudioSettings Settings { get; private set; } = LoadSettings();
    private static readonly SemaphoreSlim settingsGate = new(1, 1);
    public static event Action<StudioSettings>? SettingsChanged;

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
        ProjectFileService.Validate(project);
        var savedProject = project with
        {
            Version = CurrentProjectVersion,
            Document = UnicodeGrid.Upgrade(project.Document),
            GeneratedDocument = project.GeneratedDocument is null ? null : UnicodeGrid.Upgrade(project.GeneratedDocument)
        };
        var bytes = await Task.Run(() => JsonSerializer.SerializeToUtf8Bytes(savedProject));
        if (bytes.Length > ProjectFileService.MaximumBytes) throw new InvalidDataException("项目超过 256MB，请降低字符画尺寸或输入图片大小。");
        if (File.Exists(path))
        {
            var current = await ReadProjectVersion(path);
            if (current > CurrentProjectVersion) throw new InvalidDataException("目标文件使用更新的项目格式，请另存为新文件。");
            if (current < CurrentProjectVersion)
            {
                var backup = path + ".bak";
                await AtomicWrite(backup, await BoundedFile.ReadAsync(path, ProjectFileService.MaximumBytes));
            }
        }
        await AtomicWrite(path, bytes);
        await UpdateSettings(s => s with { RecentFiles = new[] { path }.Concat(s.RecentFiles ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToArray() });
    }

    public static async Task<StudioProject> OpenProject(string path)
    {
        var project = await ProjectFileService.Read(path);
        await UpdateSettings(s => s with { RecentFiles = new[] { path }.Concat(s.RecentFiles ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToArray() });
        return project;
    }

    private static async Task<int> ReadProjectVersion(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(BoundedFile.JsonBytes(await BoundedFile.ReadAsync(path, ProjectFileService.MaximumBytes)));
            return document.RootElement.TryGetProperty("Version", out var version) && version.TryGetInt32(out var value) ? value : 1;
        }
        catch (JsonException) { throw new InvalidDataException("旧项目文件损坏，无法创建迁移备份。"); }
    }

    public static Task SetSettings(StudioSettings settings) => UpdateSettings(_ => settings);

    private static readonly JsonSerializerOptions preferencesJson = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] ExportPreferences() => JsonSerializer.SerializeToUtf8Bytes(Settings with { RecentFiles = [] }, preferencesJson);

    public static void ValidatePreferencesExportPath(string path)
    {
        if (Path.GetFullPath(path).Equals(Path.Combine(DataDirectory, "settings.json"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请将偏好导出到另一个文件 / Export preferences to a different file.");
    }

    public static Task ImportPreferences(byte[] bytes)
    {
        if (bytes.Length > 1_000_000) throw new InvalidDataException("偏好文件超过 1 MB / Preferences exceed 1 MB.");
        var imported = JsonSerializer.Deserialize<StudioSettings>(BoundedFile.JsonBytes(bytes).Span, preferencesJson)
            ?? throw new InvalidDataException("偏好文件不能为空 / Preferences cannot be null.");
        return UpdateSettings(current => imported with { RecentFiles = current.RecentFiles });
    }

    public static Task ResetPreferences() => UpdateSettings(current => new StudioSettings(RecentFiles: current.RecentFiles));

    public static async Task UpdateSettings(Func<StudioSettings, StudioSettings> update)
    {
        StudioSettings next;
        await settingsGate.WaitAsync();
        try
        {
            next = Normalize(update(Settings));
            await AtomicWrite(Path.Combine(DataDirectory, "settings.json"), JsonSerializer.SerializeToUtf8Bytes(next));
            Settings = next;
        }
        finally { settingsGate.Release(); }
        SettingsChanged?.Invoke(next);
    }

    private static StudioSettings Normalize(StudioSettings settings) => settings with
    {
        UiLanguage = settings.UiLanguage is "system" or "zh-CN" or "en-US" ? settings.UiLanguage : "system",
        Theme = settings.Theme is "Light" or "Dark" or "System" ? settings.Theme : "Dark",
        PreviewFontSize = double.IsFinite(settings.PreviewFontSize) ? Math.Clamp(settings.PreviewFontSize, 8, 30) : 13,
        PreviewZoom = double.IsFinite(settings.PreviewZoom) ? Math.Clamp(settings.PreviewZoom, .25, 4) : 1,
        UiFontSize = double.IsFinite(settings.UiFontSize) ? Math.Clamp(settings.UiFontSize, 12, 24) : 14,
        UiFontFamily = !string.IsNullOrWhiteSpace(settings.UiFontFamily) && settings.UiFontFamily.Length <= 128 && !settings.UiFontFamily.Any(char.IsControl) ? settings.UiFontFamily : "Segoe UI",
        ExportScale = Math.Clamp(settings.ExportScale, 1, 4),
        DefaultColumns = Math.Clamp(settings.DefaultColumns, 8, ImageResourceLimits.GridSide),
        ConversionDelay = Math.Clamp(settings.ConversionDelay, 0, 1000),
        FavoriteSettings = SettingsCatalog.NormalizeFavorites(settings.FavoriteSettings),
        DefaultExportFormat = new[] { "TXT", "PNG", "JPEG", "GIF", "HTML", "SVG", "ANSI", "JSON", "Markdown" }.Contains(settings.DefaultExportFormat) ? settings.DefaultExportFormat : "TXT",
        FilePrefix = new string((settings.FilePrefix ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)).Take(32).ToArray()),
        RecentFiles = (settings.RecentFiles ?? []).Where(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 32767 && !p.Contains('\0')).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToArray()
    };

    private static StudioSettings LoadSettings()
    {
        try { return Normalize(JsonSerializer.Deserialize<StudioSettings>(BoundedFile.JsonBytes(BoundedFile.Read(Path.Combine(DataDirectory, "settings.json"), 1_000_000)).Span) ?? new()); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { return new(); }
    }
}
