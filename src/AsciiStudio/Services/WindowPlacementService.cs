using System.Text.Json;

namespace AsciiStudio.Services;

public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized);

public static class WindowPlacementService
{
    private static string FilePath => Path.Combine(WorkspaceService.DataDirectory, "window.json");

    public static WindowPlacement? Load()
    {
        try
        {
            var saved = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(FilePath));
            return saved is { Width: >= 480 and <= 10000, Height: >= 480 and <= 10000 } ? saved : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    // This tiny file is written once when the window closes, independently of
    // project/theme settings, so concurrent autosave cannot overwrite placement.
    public static void Save(WindowPlacement placement)
    {
        Directory.CreateDirectory(WorkspaceService.DataDirectory);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(placement));
            File.Move(temp, FilePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
