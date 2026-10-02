using System.Text.Json;

namespace AsciiStudio.Services;

public sealed record ProjectSessionEntry(string Id, string Mode, string? Path, string Title);
public sealed record ProjectSessionState(ProjectSessionEntry[] Tabs, string? ActiveId, ProjectSessionEntry[]? Recoveries = null);

public static class WorkspaceSessionService
{
    private static readonly SemaphoreSlim saveGate = new(1, 1);
    private static string SessionPath => Path.Combine(WorkspaceService.DataDirectory, "session.json");
    private static string RecoveryDirectory => Path.Combine(WorkspaceService.DataDirectory, "recovery");

    public static ProjectSessionState Load()
    {
        try
        {
            if (new FileInfo(SessionPath).Length > 1_000_000) return new([], null);
            var state = JsonSerializer.Deserialize<ProjectSessionState>(File.ReadAllText(SessionPath)) ?? new([], null);
            return state with { Tabs = (state.Tabs ?? []).Where(IsValid).Take(32).ToArray(), Recoveries = (state.Recoveries ?? []).Where(IsValid).Take(100).ToArray() };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new([], null); }
    }

    public static async Task Save(ProjectSessionEntry[] tabs, string? activeId, ProjectSessionEntry[] recoveries)
    {
        await saveGate.WaitAsync();
        try { await WorkspaceService.AtomicWrite(SessionPath, JsonSerializer.SerializeToUtf8Bytes(new ProjectSessionState(tabs, activeId, recoveries))); }
        finally { saveGate.Release(); }
    }

    private static bool IsValid(ProjectSessionEntry entry) => entry is not null && ValidId(entry.Id) && entry.Title?.Length <= 512;
    private static bool ValidId(string id) => id is { Length: >= 8 and <= 64 } && id.All(char.IsAsciiHexDigit);
    public static string RecoveryPath(string id) => ValidId(id) ? Path.Combine(RecoveryDirectory, id + ".asciiproj") : throw new ArgumentException("无效会话标识。");

    public static bool TryLoadRecovery(string id, out StudioProject? project)
    {
        try
        {
            var path = RecoveryPath(id);
            if (new FileInfo(path).Length > 100_000_000) { project = null; return false; }
            project = JsonSerializer.Deserialize<StudioProject>(File.ReadAllBytes(path));
            if (project?.Document is null) { project = null; return false; }
            if (project.Version is < 1 or > WorkspaceService.CurrentProjectVersion) { project = null; return false; }
            project.Document.Validate(); project.GeneratedDocument?.Validate(); project.Geometry?.Validate();
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { project = null; return false; }
    }

    public static void DeleteRecovery(string id)
    {
        try { File.Delete(RecoveryPath(id)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
