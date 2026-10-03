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
            var state = JsonSerializer.Deserialize<ProjectSessionState>(BoundedFile.JsonBytes(BoundedFile.Read(SessionPath, 1_000_000)).Span) ?? new([], null);
            return Normalize(state);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { return new([], null); }
    }

    public static async Task Save(ProjectSessionEntry[] tabs, string? activeId, ProjectSessionEntry[] recoveries)
    {
        await saveGate.WaitAsync();
        try { await WorkspaceService.AtomicWrite(SessionPath, JsonSerializer.SerializeToUtf8Bytes(Normalize(new(tabs, activeId, recoveries)))); }
        finally { saveGate.Release(); }
    }

    public static ProjectSessionState Normalize(ProjectSessionState state)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tabs = (state.Tabs ?? []).Where(IsValid).DistinctBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .Where(e => e.Path is null || paths.Add(Path.GetFullPath(e.Path))).Take(32).ToArray();
        var recoveries = (state.Recoveries ?? []).Where(IsValid).Where(e => !tabs.Any(t => t.Id.Equals(e.Id, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(e => e.Id, StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
        return new(tabs, tabs.Any(e => e.Id == state.ActiveId) ? state.ActiveId : tabs.FirstOrDefault()?.Id, recoveries);
    }
    private static bool IsValid(ProjectSessionEntry entry)
    {
        if (entry is null || !ValidId(entry.Id) || entry.Title is null || entry.Title.Length > 512 || entry.Mode is not ("image" or "text" or "ansi" or "generator")) return false;
        if (entry.Path is null) return true;
        try { _ = Path.GetFullPath(entry.Path); return entry.Path.Length <= 32767 && Path.IsPathFullyQualified(entry.Path) && !entry.Path.Contains('\0'); }
        catch (ArgumentException) { return false; }
    }
    private static bool ValidId(string id) => id is { Length: >= 8 and <= 64 } && id.All(char.IsAsciiHexDigit);
    public static string RecoveryPath(string id) => ValidId(id) ? Path.Combine(RecoveryDirectory, id + ".asciiproj") : throw new ArgumentException("无效会话标识。");

    public static bool TryLoadRecovery(string id, out StudioProject? project)
    {
        try
        {
            var path = RecoveryPath(id);
            project = ProjectFileService.Parse(BoundedFile.Read(path, ProjectFileService.MaximumBytes));
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
