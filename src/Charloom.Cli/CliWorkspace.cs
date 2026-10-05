using System.Text.Json;
using Charloom.Services;

namespace Charloom.Cli;

public sealed record CliWorkspaceState(int Schema, string[] Projects, string? Active, string[] Recovery, string[] Recent);
public sealed class CliWorkspace : IDisposable
{
    public const int MaximumBytes = 1_000_000;
    private readonly string path;
    private readonly FileStream gate;
    public CliWorkspaceState State { get; private set; }
    private CliWorkspace(string path, FileStream gate, CliWorkspaceState state) { this.path = path; this.gate = gate; State = state; }
    public static string DefaultPath => Path.Combine(WorkspaceService.DataDirectory, "cli-workspace.json");
    public static async Task<CliWorkspace> Open(string? path, CancellationToken token)
    {
        path = Path.GetFullPath(path ?? DefaultPath); var gate = ProjectEditSession.Lock(path + ".lock");
        try
        {
            var state = File.Exists(path) ? JsonSerializer.Deserialize<CliWorkspaceState>(BoundedFile.JsonBytes(await BoundedFile.ReadAsync(path, MaximumBytes, token)).Span, CliArguments.Json)
                ?? throw new InvalidDataException("Workspace is empty.") : new(1, [], null, [], []);
            Validate(state); return new(path, gate, state);
        }
        catch { gate.Dispose(); throw; }
    }
    private static void Validate(CliWorkspaceState state)
    {
        if (state.Schema != 1 || state.Projects is null || state.Recovery is null || state.Recent is null
            || state.Projects.Length > 32 || state.Recovery.Length > 32 || state.Recent.Length > 15
            || state.Projects.Concat(state.Recovery).Concat(state.Recent).Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 32767 || !Path.IsPathFullyQualified(p))
            || state.Projects.Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Projects.Length
            || state.Recovery.Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Recovery.Length
            || state.Recent.Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Recent.Length
            || state.Active is not null && !state.Projects.Contains(state.Active, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid CLI workspace.");
    }
    public static byte[] Serialize(CliWorkspaceState state)
    {
        Validate(state);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("CLI workspace exceeds its 1 MB limit.");
        return bytes;
    }
    public async Task Persist(CancellationToken token)
    { await ProjectEditSession.Atomic(path, Serialize(State), true, token); }
    public async Task Add(string project, CancellationToken token)
    {
        project = Path.GetFullPath(project);
        if (project.Equals(path, StringComparison.OrdinalIgnoreCase) || project.Equals(path + ".lock", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Project must not replace the workspace manifest.");
        using var session = await ProjectEditSession.Open(project, token);
        var projects = State.Projects.Contains(project, StringComparer.OrdinalIgnoreCase) ? State.Projects : [.. State.Projects, project];
        if (projects.Length > 32) throw new CliUsageException("Workspace allows at most 32 open projects.");
        State = State with { Projects = projects, Active = project,
            Recovery = State.Recovery.Where(p => !p.Equals(project, StringComparison.OrdinalIgnoreCase)).ToArray(),
            Recent = new[] { project }.Concat(State.Recent).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToArray() };
        await Persist(token);
    }
    public async Task Activate(string project, CancellationToken token)
    {
        project = Path.GetFullPath(project);
        if (!State.Projects.Contains(project, StringComparer.OrdinalIgnoreCase)) throw new CliUsageException("Project is not open in this workspace.");
        using var session = await ProjectEditSession.Open(project, token);
        State = State with { Active = project }; await Persist(token);
    }
    public async Task Close(string? project, string action, bool overwrite, CancellationToken token)
    {
        if (action is not ("save" or "keep" or "cancel")) throw new CliUsageException("--action: save | keep | cancel");
        project = Path.GetFullPath(project ?? State.Active ?? throw new CliUsageException("No active project."));
        if (!State.Projects.Contains(project, StringComparer.OrdinalIgnoreCase)) throw new CliUsageException("Project is not open.");
        using var session = await OpenForClose(project, action, token);
        var dirty = session is null ? File.Exists(ProjectEditSession.StatePath(project)) : session.Dirty || session.State.Candidate is not null;
        if (dirty && action == "cancel") throw new CliUsageException("Unsaved edits: choose --action save --overwrite or --action keep.");
        var recovery = State.Recovery;
        if (dirty && action == "save") await session!.Save(project, overwrite, token);
        if (dirty && action == "keep" || action == "save" && session?.State.Candidate is not null)
        {
            recovery = new[] { project }.Concat(recovery).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (recovery.Length > 32) throw new CliUsageException("Recovery list is full; restore a project before closing another.");
            if (session is not null) await session.Persist(token);
        }
        var projects = State.Projects.Where(p => !p.Equals(project, StringComparison.OrdinalIgnoreCase)).ToArray();
        State = State with { Projects = projects, Recovery = recovery, Active = State.Active?.Equals(project, StringComparison.OrdinalIgnoreCase) == true ? projects.LastOrDefault() : State.Active };
        await Persist(token);
    }
    public async Task<object[]> Status(CancellationToken token)
    {
        var result = new List<object>();
        foreach (var project in State.Projects)
        {
            try { using var session = await ProjectEditSession.Open(project, token); result.Add(new { path = project, active = project.Equals(State.Active, StringComparison.OrdinalIgnoreCase), available = true, state = session.Summary() }); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
            { result.Add(new { path = project, available = false, error = ex.Message }); }
        }
        return result.ToArray();
    }
    public async Task<(int Failures, object[] Results)> Restore(CancellationToken token)
    {
        var results = new List<object>(); var failures = 0;
        foreach (var project in State.Recovery.ToArray())
        {
            token.ThrowIfCancellationRequested();
            try { await Add(project, token); results.Add(new { path = project, restored = true }); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or CliUsageException)
            { failures++; results.Add(new { path = project, restored = false, error = ex.Message }); }
        }
        return (failures, results.ToArray());
    }
    public async Task ClearRecent(CancellationToken token)
    { State = State with { Recent = Array.Empty<string>() }; await Persist(token); }
    public void Dispose() => gate.Dispose();
    private static async Task<ProjectEditSession?> OpenForClose(string project, string action, CancellationToken token)
    {
        try { return await ProjectEditSession.Open(project, token); }
        catch (Exception ex) when (action == "keep" && ex is not CliConflictException { Busy: true }
            && ex is (IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException))
        { return null; }
    }
}
