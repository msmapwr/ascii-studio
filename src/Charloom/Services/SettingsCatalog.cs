namespace Charloom.Services;

public sealed record SettingEntry(string Key, string AutomationId, string Name, string EnglishName, string Group, string EnglishGroup);

/// <summary>Stable preference keys and bilingual search metadata shared by desktop and CLI.</summary>
public static class SettingsCatalog
{
    public static IReadOnlyList<SettingEntry> Entries { get; } = Array.AsReadOnly<SettingEntry>([
        new("UiLanguage", "SettingsUiLanguage", "界面语言", "Interface language", "常用与外观", "Appearance"),
        new("Theme", "SettingsTheme", "主题", "Theme", "常用与外观", "Appearance"),
        new("Animations", "SettingsAnimations", "动画（遵循 Windows 设置）", "Animations (follow Windows settings)", "常用与外观", "Appearance"),
        new("BeginnerMode", "SettingsBeginner", "新手模式", "Beginner mode", "常用与外观", "Appearance"),
        new("UiFontFamily", "SettingsUiFontContainer", "界面字体", "Interface font", "常用与外观", "Appearance"),
        new("UiFontSize", "SettingsUiFontSize", "界面字号", "Interface font size", "常用与外观", "Appearance"),
        new("CompactLayout", "SettingsCompact", "紧凑布局", "Compact layout", "常用与外观", "Appearance"),
        new("RememberWindow", "SettingsRememberWindow", "记住窗口尺寸与位置", "Remember window size and position", "常用与外观", "Appearance"),
        new("PreviewFontSize", "SettingsFontSize", "默认字号", "Default artwork font size", "编辑", "Editing"),
        new("PreviewZoom", "SettingsPreviewZoom", "默认预览缩放", "Default preview zoom", "编辑", "Editing"),
        new("WordWrap", "SettingsWordWrap", "结果自动换行", "Wrap result text", "编辑", "Editing"),
        new("ShowStats", "SettingsShowStats", "显示结果统计", "Show result statistics", "编辑", "Editing"),
        new("AutoConvert", "SettingsAutoConvert", "调整参数后自动转换", "Convert automatically after parameter changes", "图片转换", "Image conversion"),
        new("ConversionDelay", "SettingsDelay", "自动转换等待时间（毫秒）", "Automatic conversion delay (milliseconds)", "图片转换", "Image conversion"),
        new("DefaultColumns", "SettingsColumns", "默认列数", "Default columns", "图片转换", "Image conversion"),
        new("DefaultExportFormat", "SettingsExportFormat", "默认格式", "Default export format", "导出", "Export"),
        new("ExportScale", "SettingsExportScale", "默认图片倍率", "Default bitmap scale", "导出", "Export"),
        new("FilePrefix", "SettingsFilePrefix", "文件名前缀", "Filename prefix", "导出", "Export")
    ]);

    public static SettingEntry Get(string key) => Entries.FirstOrDefault(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("未知设置键 / Unknown setting key: " + key);

    public static string[] NormalizeFavorites(IEnumerable<string>? keys) => (keys ?? [])
        .Select(key => Entries.FirstOrDefault(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Key)
        .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();

    public static StudioSettings SetFavorite(StudioSettings settings, string key, bool favorite)
    {
        var canonical = Get(key).Key;
        var keys = NormalizeFavorites(settings.FavoriteSettings).ToList();
        if (favorite && !keys.Contains(canonical)) keys.Add(canonical);
        if (!favorite) keys.Remove(canonical);
        return settings with { FavoriteSettings = keys.ToArray() };
    }

    public static IEnumerable<SettingEntry> Search(string? query, StudioSettings settings, bool favoritesOnly = false)
    {
        if (query?.Length > 256) throw new ArgumentException("设置搜索最多 256 字符 / Setting search is limited to 256 characters.");
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var favorites = NormalizeFavorites(settings.FavoriteSettings);
        return Entries.Where(entry => (!favoritesOnly || favorites.Contains(entry.Key)) && words.All(word =>
            $"{entry.Key} {entry.Name} {entry.EnglishName} {entry.Group} {entry.EnglishGroup}".Contains(word, StringComparison.OrdinalIgnoreCase)));
    }
}
