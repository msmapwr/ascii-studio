namespace Charloom.Services;

public static class ProductIdentity
{
    public const string Name = "Charloom";
    public const string ChineseName = "字织";
    public const string DisplayName = "Charloom｜字织";
    public const string Subtitle = "离线 ASCII / ANSI 字符艺术创作工具";
    public const string Tagline = "把字符织成画面";
    // Preserve the existing data location, independently of executable/brand names.
    public static string DesktopDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AsciiStudio");
    public static string? EnvironmentValue(string modern, string legacy) => Environment.GetEnvironmentVariable(modern) is { Length: > 0 } value
        ? value : Environment.GetEnvironmentVariable(legacy);
}
