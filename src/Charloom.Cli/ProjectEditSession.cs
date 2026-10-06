using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Charloom.Core;
using Charloom.Services;

namespace Charloom.Cli;

public sealed class CliConflictException(string message, bool busy = false) : IOException(message)
{ public bool Busy { get; } = busy; }
public sealed record EditRevision(AsciiDocument Document, bool Edited,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AsciiDocument? Generated = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImageGeometry? Geometry = null);
public sealed record GeometryEditState(List<ImageGeometry> Revisions, int Index);
public sealed record EditSelection(int Row, int Column, int EndRow, int EndColumn, bool Rectangle = false);
public sealed record EditState(int Schema, string BaseDigest, List<EditRevision> Revisions, int Index,
    string SavedDigest, AsciiDocument Generated, EditSelection? Selection = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AsciiDocument? Candidate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GeometryEditState? GeometryHistory = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImageGeometry? CandidateGeometry = null);

/// <summary>Each invocation locks its sidecar, verifies the source, and commits atomically.</summary>
public sealed class ProjectEditSession : IDisposable
{
    public const int MaximumSteps = 100;
    public const long MaximumHistoryBytes = 64L * 1024 * 1024;
    public const int MaximumStateBytes = ProjectFileService.MaximumBytes;
    private readonly FileStream gate;
    public string Path { get; }
    public StudioProject Source { get; private set; }
    public EditState State { get; private set; }
    public StudioProject Current => Source with { Document = State.Revisions[State.Index].Document,
        Edited = State.Revisions[State.Index].Edited, GeneratedDocument = State.Revisions[State.Index].Generated ?? State.Generated,
        Geometry = State.Revisions[State.Index].Geometry ?? Source.Geometry };
    public StudioProject CandidateProject => Current with { Document = State.Candidate ?? throw new CliUsageException("No candidate; use candidate create."),
        Edited = false, GeneratedDocument = null, Geometry = State.CandidateGeometry ?? Current.Geometry };
    public ImageGeometry DraftGeometry => State.GeometryHistory is { } history ? history.Revisions[history.Index] : Current.Geometry ?? new();
    public bool GeometryPending => Source.Mode == "image" && DraftGeometry != (Current.Geometry ?? new());
    public bool Dirty => Digest(State.Revisions[State.Index]) != State.SavedDigest;
    public bool CanUndo => State.Index > 0;
    public bool CanRedo => State.Index < State.Revisions.Count - 1;
    public long RetainedBytes => State.Revisions.Sum(Size);
    public static string StatePath(string path) => System.IO.Path.GetFullPath(path) + ".cli-state.json";
    private ProjectEditSession(string path, FileStream gate, StudioProject source, EditState state)
    { Path = path; this.gate = gate; Source = source; State = state; }
    public static FileStream Lock(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new CliConflictException("State is busy in another process. Retry after it finishes.", true); }
    }
    public static async Task<ProjectEditSession> Open(string path, CancellationToken token = default, bool reload = false)
    {
        path = System.IO.Path.GetFullPath(path);
        // Do not create directories or locks for missing project paths.
        if (!File.Exists(path)) throw new FileNotFoundException("Project file not found.", path);
        var gate = Lock(StatePath(path) + ".lock");
        try
        {
            var bytes = await BoundedFile.ReadAsync(path, ProjectFileService.MaximumBytes, token);
            var source = ProjectFileService.Parse(bytes); var digest = Digest(bytes);
            var revision = new EditRevision(source.Document, source.Edited);
            var state = new EditState(2, digest, [revision], 0, Digest(revision), source.GeneratedDocument ?? source.Document);
            if (!reload && File.Exists(StatePath(path)))
            {
                state = JsonSerializer.Deserialize<EditState>(BoundedFile.JsonBytes(await BoundedFile.ReadAsync(StatePath(path), MaximumStateBytes, token)).Span, CliArguments.Json)
                    ?? throw new InvalidDataException("Edit state is empty.");
                Validate(state);
                if (state.BaseDigest != digest) throw new CliConflictException("Project changed outside this edit session. Use project recover --output NEW_PATH or project reload --discard-edits.");
                state = state with { Schema = Math.Max(state.Schema, 2) };
            }
            if (source.Mode == "image")
            {
                if (state.Schema == 3 && (state.GeometryHistory is null || state.Revisions.Any(r => r.Geometry is null)
                    || state.Candidate is not null && state.CandidateGeometry is null)) throw new InvalidDataException("Incomplete image geometry state.");
                if (state.Schema < 3)
                {
                    // Capture absolute geometry before a save can change Source.Geometry.
                    // Rehash only during migration, not on every image invocation.
                    var saved = state.Revisions.FindIndex(r => Digest(r) == state.SavedDigest);
                    var revisions = state.Revisions.Select(r => r with { Geometry = source.Geometry ?? new() }).ToList();
                    state = state with { Schema = 3, Revisions = revisions,
                        SavedDigest = saved < 0 ? state.SavedDigest : Digest(revisions[saved]),
                        GeometryHistory = new([revisions[state.Index].Geometry!], 0),
                        CandidateGeometry = state.Candidate is null ? null : source.Geometry ?? new() };
                }
            }
            return new(path, gate, source, state);
        }
        catch { gate.Dispose(); throw; }
    }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Digest(EditRevision value) => Digest(JsonSerializer.SerializeToUtf8Bytes(value));
    private static long DocumentSize(AsciiDocument value) => value.Text.Length * 2L + (value.Colors?.LongLength ?? 0) * 4L + (value.BackgroundColors?.LongLength ?? 0) * 4L;
    private static long Size(EditRevision value) => Math.Max(JsonSerializer.SerializeToUtf8Bytes(value).LongLength,
        512L + DocumentSize(value.Document) + (value.Generated is null ? 0 : DocumentSize(value.Generated)));
    private static void Validate(EditState state)
    {
        if (state.Schema is not (1 or 2 or 3) || state.Revisions is null || state.Revisions.Count is < 1 or > MaximumSteps + 1
            || state.Index < 0 || state.Index >= state.Revisions.Count || state.Generated is null
            || !ValidDigest(state.BaseDigest) || !ValidDigest(state.SavedDigest)) throw new InvalidDataException("Invalid edit state.");
        state.Generated.Validate();
        if (state.Generated.GridVersion != 1) throw new InvalidDataException("Edit state requires Unicode grid version 1.");
        foreach (var revision in state.Revisions)
        {
            if (revision?.Document is null || revision.Document.GridVersion != 1) throw new InvalidDataException("Invalid edit revision.");
            revision.Document.Validate();
            revision.Geometry?.Validate();
            if (revision.Generated is { } generated) { generated.Validate(); if (generated.GridVersion != 1) throw new InvalidDataException("Invalid generated revision."); }
        }
        if (state.Candidate is { } candidate) { candidate.Validate(); if (candidate.GridVersion != 1) throw new InvalidDataException("Invalid candidate grid."); }
        if (state.Schema == 1 && (state.Candidate is not null || state.Revisions.Any(r => r.Generated is not null))) throw new InvalidDataException("Candidate state requires schema 2.");
        if (state.Schema < 3 && (state.GeometryHistory is not null || state.CandidateGeometry is not null || state.Revisions.Any(r => r.Geometry is not null)))
            throw new InvalidDataException("Geometry state requires schema 3.");
        if (state.GeometryHistory is { } geometry)
        {
            if (geometry.Revisions is null || geometry.Revisions.Count is < 1 or > 41 || geometry.Index < 0 || geometry.Index >= geometry.Revisions.Count)
                throw new InvalidDataException("Invalid geometry history.");
            foreach (var value in geometry.Revisions) (value ?? throw new InvalidDataException("Missing geometry revision.")).Validate();
        }
        state.CandidateGeometry?.Validate();
        if (state.Candidate is null && state.CandidateGeometry is not null) throw new InvalidDataException("Geometry without a candidate.");
        if (state.Revisions.Count > 1 && state.Revisions.Sum(Size) > MaximumHistoryBytes) throw new InvalidDataException("History exceeds 64MB.");
        if (state.Selection is { } selection) TextEditOperations.ValidateSelection(state.Revisions[state.Index].Document.Text, selection);
    }
    private static bool ValidDigest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public async Task Persist(CancellationToken token)
    {
        Validate(State); token.ThrowIfCancellationRequested();
        var sourceBytes = await BoundedFile.ReadAsync(Path, ProjectFileService.MaximumBytes, token);
        if (Digest(sourceBytes) != State.BaseDigest) throw new CliConflictException("Project changed before state commit; no edit was written.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(State);
        if (bytes.Length > MaximumStateBytes) throw new InvalidDataException("Edit state exceeds 256MB; reduce the result size.");
        await Atomic(StatePath(Path), bytes, true, token);
    }
    public async Task Edit(string text, CancellationToken token)
    {
        var previous = Current.Document;
        var document = AsciiDocument.FromText(text, previous.Title) with { FontFamily = previous.FontFamily, CellWidth = previous.CellWidth, CellHeight = previous.CellHeight };
        if (document.Text == previous.Text) return;
        var current = State.Revisions[State.Index];
        var revisions = TrimHistory(State.Revisions.Take(State.Index + 1).Append(new EditRevision(document, true, current.Generated, current.Geometry)).ToArray());
        State = State with { Revisions = revisions, Index = revisions.Count - 1, Selection = null };
        await Persist(token);
    }
    public static List<EditRevision> TrimHistory(IReadOnlyList<EditRevision> revisions, int maximumSteps = MaximumSteps, long maximumBytes = MaximumHistoryBytes)
    {
        if (revisions.Count == 0 || maximumSteps < 1 || maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(revisions));
        var sizes = revisions.Select(Size).ToArray(); var retained = sizes.Sum(); var start = 0;
        while (start < revisions.Count - 1 && (revisions.Count - start > maximumSteps + 1 || retained > maximumBytes)) retained -= sizes[start++];
        return revisions.Skip(start).ToList();
    }
    public async Task Move(bool redo, CancellationToken token)
    {
        if (redo ? !CanRedo : !CanUndo) throw new CliUsageException(redo ? "Nothing to redo." : "Nothing to undo.");
        State = State with { Index = State.Index + (redo ? 1 : -1), Selection = null }; await Persist(token);
    }
    public async Task Select(EditSelection selection, CancellationToken token)
    {
        TextEditOperations.ValidateSelection(Current.Document.Text, selection);
        State = State with { Selection = selection }; await Persist(token);
    }
    public async Task ClearHistory(CancellationToken token)
    { State = State with { Revisions = [State.Revisions[State.Index]], Index = 0 }; await Persist(token); }
    private GeometryEditState ImageHistory()
    {
        if (Source.Mode != "image" || Source.SourceImage is null) throw new CliUsageException("Geometry requires an image project with its source.");
        return State.GeometryHistory ?? new([Current.Geometry ?? new()], 0);
    }
    public object GeometrySummary()
    {
        var history = ImageHistory();
        return new { path = Path, current = DraftGeometry, applied = Current.Geometry, pending = GeometryPending,
            count = history.Revisions.Count, index = history.Index, canUndo = history.Index > 0,
            canRedo = history.Index < history.Revisions.Count - 1, maximumSteps = 40, candidateGeometry = State.CandidateGeometry };
    }
    public async Task SetGeometry(ImageGeometry geometry, CancellationToken token)
    {
        var history = ImageHistory(); geometry.Validate();
        if (geometry == DraftGeometry) return;
        var revisions = history.Revisions.Take(history.Index + 1).Append(geometry).TakeLast(41).ToList();
        State = State with { Schema = 3, GeometryHistory = new(revisions, revisions.Count - 1) };
        await Persist(token);
    }
    public async Task MoveGeometry(bool redo, CancellationToken token)
    {
        var history = ImageHistory();
        if (redo ? history.Index >= history.Revisions.Count - 1 : history.Index == 0)
            throw new CliUsageException(redo ? "No geometry to redo." : "No geometry to undo.");
        State = State with { GeometryHistory = history with { Index = history.Index + (redo ? 1 : -1) } };
        await Persist(token);
    }
    public async Task SetCandidate(AsciiDocument document, bool replace, CancellationToken token, ImageGeometry? geometry = null)
    {
        if (State.Candidate is not null && !replace) throw new CliConflictException("Candidate already exists; explicit --replace-candidate required.");
        State = State with { Schema = Math.Max(State.Schema, geometry is null ? 2 : 3), Candidate = document,
            CandidateGeometry = geometry ?? (Source.Mode == "image" ? Current.Geometry : null) }; await Persist(token);
    }
    public async Task AcceptCandidate(CancellationToken token)
    {
        var project = CandidateProject; var document = project.Document;
        var revisions = TrimHistory(State.Revisions.Take(State.Index + 1).Append(new EditRevision(document, false, document, project.Geometry)).ToArray());
        State = State with { Schema = Math.Max(State.Schema, 2), Revisions = revisions, Index = revisions.Count - 1,
            Selection = null, Candidate = null, CandidateGeometry = null };
        await Persist(token);
    }
    public async Task DiscardCandidate(CancellationToken token)
    {
        _ = CandidateProject;
        State = State with { Candidate = null, CandidateGeometry = null }; await Persist(token);
    }
    public async Task SaveCandidate(string destination, bool overwrite, CancellationToken token)
    {
        destination = System.IO.Path.GetFullPath(destination);
        if (destination.Equals(Path, StringComparison.OrdinalIgnoreCase) || destination.Equals(StatePath(Path), StringComparison.OrdinalIgnoreCase)
            || destination.Equals(StatePath(Path) + ".lock", StringComparison.OrdinalIgnoreCase)) throw new CliUsageException("Save candidate to an independent project path.");
        if (File.Exists(StatePath(destination))) throw new CliConflictException("Destination has an edit session; choose another path.");
        var project = CandidateProject; ProjectFileService.Validate(project);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project);
        if (bytes.Length > ProjectFileService.MaximumBytes) throw new InvalidDataException("Candidate project exceeds 256MB.");
        await Atomic(destination, bytes, overwrite, token);
    }
    public async Task Save(string destination, bool overwrite, CancellationToken token)
    {
        destination = System.IO.Path.GetFullPath(destination);
        if (File.Exists(destination) && !overwrite) throw new IOException("Output exists; explicit --overwrite required.");
        if (destination.Equals(StatePath(Path), StringComparison.OrdinalIgnoreCase) || destination.Equals(StatePath(Path) + ".lock", StringComparison.OrdinalIgnoreCase)) throw new CliUsageException("Project output must not replace edit state.");
        var same = destination.Equals(Path, StringComparison.OrdinalIgnoreCase);
        var project = Current; ProjectFileService.Validate(project);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project);
        if (bytes.Length > ProjectFileService.MaximumBytes) throw new InvalidDataException("Project exceeds 256MB.");
        if (same)
        {
            if (Digest(await BoundedFile.ReadAsync(Path, ProjectFileService.MaximumBytes, token)) != State.BaseDigest)
                throw new CliConflictException("Project changed before save.");
            // Shared writer preserves the existing old-format migration backup policy.
            token.ThrowIfCancellationRequested(); await WorkspaceService.SaveProject(destination, project);
            State = State with { BaseDigest = Digest(await BoundedFile.ReadAsync(destination, ProjectFileService.MaximumBytes, token)), SavedDigest = Digest(State.Revisions[State.Index]) };
            Source = project; await Persist(token);
        }
        else
        {
            if (File.Exists(StatePath(destination))) throw new CliConflictException("Destination has an edit session; choose another project path.");
            await Atomic(destination, bytes, overwrite, token);
        }
    }
    public object Summary() => new { path = Path, dirty = Dirty, edited = Current.Edited, count = State.Revisions.Count,
        index = State.Index, canUndo = CanUndo, canRedo = CanRedo, retainedBytes = RetainedBytes, maximumSteps = MaximumSteps,
        maximumBytes = MaximumHistoryBytes, selection = State.Selection, Current.Document.Width, Current.Document.Height,
        geometryPending = GeometryPending,
        candidate = State.Candidate is { } document ? new { document.Width, document.Height, document.Title } : null };
    public static async Task<StudioProject> Read(string path, CancellationToken token)
    {
        if (!File.Exists(StatePath(path))) return await ProjectFileService.Read(path, token);
        using var session = await Open(path, token); return session.Current;
    }
    public static async Task<object> Recover(string path, string destination, CancellationToken token)
    {
        path = System.IO.Path.GetFullPath(path); destination = System.IO.Path.GetFullPath(destination);
        if (destination.Equals(path, StringComparison.OrdinalIgnoreCase) || destination.Equals(StatePath(path), StringComparison.OrdinalIgnoreCase)
            || destination.Equals(StatePath(path) + ".lock", StringComparison.OrdinalIgnoreCase)) throw new CliUsageException("Recover to a new independent project path.");
        if (!File.Exists(StatePath(path))) throw new FileNotFoundException("No edit sidecar to recover.");
        using var sourceGate = Lock(StatePath(path) + ".lock");
        using var targetGate = Lock(StatePath(destination) + ".lock");
        if (File.Exists(destination) || File.Exists(StatePath(destination))) throw new IOException("Recovery destination already exists; choose a new path.");
        var state = JsonSerializer.Deserialize<EditState>(BoundedFile.JsonBytes(await BoundedFile.ReadAsync(StatePath(path), MaximumStateBytes, token)).Span, CliArguments.Json)
            ?? throw new InvalidDataException("Edit state is empty.");
        Validate(state);
        // An external source replacement invalidates its metadata. Recover artwork and
        // history as a snapshot, never attach edits to an unrelated new image/text source.
        var revision = state.Revisions[state.Index];
        var project = new StudioProject(WorkspaceService.CurrentProjectVersion, revision.Document, null, null, null, "snapshot",
            Edited: revision.Edited, GeneratedDocument: revision.Generated ?? state.Generated);
        ProjectFileService.Validate(project);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project);
        if (bytes.Length > ProjectFileService.MaximumBytes) throw new InvalidDataException("Recovered project exceeds 256MB.");
        // Geometry belongs to the invalidated image source, not the recovered snapshot.
        var revisions = state.Revisions.Select(r => r with { Geometry = null }).ToList();
        state = state with { BaseDigest = Digest(bytes), Revisions = revisions, SavedDigest = Digest(revisions[state.Index]),
            GeometryHistory = null, CandidateGeometry = null };
        var stateBytes = JsonSerializer.SerializeToUtf8Bytes(state);
        if (stateBytes.Length > MaximumStateBytes) throw new InvalidDataException("Recovered history exceeds 256MB.");
        await Atomic(destination, bytes, false, token); await Atomic(StatePath(destination), stateBytes, false, token);
        return new { path = destination, recovered = true, sourcePreserved = false, historyPreserved = true };
    }
    public static async Task Atomic(string path, byte[] bytes, bool overwrite, CancellationToken token)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!; Directory.CreateDirectory(directory);
        var temp = System.IO.Path.Combine(directory, ".cli-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, token);
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { File.Move(temp, path, overwrite); break; }
                // Windows can briefly deny rename/delete while a file is inspected.
                // Keep the old file intact, retry only these native errors, and fail
                // after 150ms rather than hiding persistent permission problems.
                catch (Exception failure) when (OperatingSystem.IsWindows() && attempt < 3
                    && (failure is UnauthorizedAccessException && (failure.HResult & 0xffff) == 5
                        || failure is IOException && (failure.HResult & 0xffff) is 32 or 33))
                { await Task.Delay(50, token); }
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Dispose() => gate.Dispose();
}
