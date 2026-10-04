using System.Text.Json;
using AsciiStudio.Cli;
using AsciiStudio.Core;
using AsciiStudio.Services;
using Xunit;

namespace AsciiStudio.Cli.Tests;

[Collection("CLI")]
public sealed class EditingTests(CliFixture fixture)
{
    private static async Task<(int Exit, string Out, string Error)> Run(params string[] args)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await CliHost.Run(args, output, error, new StringReader("")); return (exit, output.ToString(), error.ToString());
    }
    private async Task<string> Project(string text = "abc\ndef")
    {
        var path = fixture.PathFor(Guid.NewGuid().ToString("N") + ".asciiproj");
        await WorkspaceService.SaveProject(path, new(4, AsciiDocument.FromText(text), null, null, null, "snapshot")); return path;
    }
    private static JsonElement Result(string json) => JsonDocument.Parse(json).RootElement.GetProperty("result");
    [Fact]
    public void WorkspaceSerializationRejectsOversizedManifest()
    {
        var paths = Enumerable.Range(0, 32).Select(i => @"D:\" + new string('a', 32000) + i).ToArray();
        Assert.Throws<InvalidDataException>(() => CliWorkspace.Serialize(new(1, paths, paths[0], [], [])));
        Assert.True(CliWorkspace.Serialize(new(1, [], null, [], [])).Length < CliWorkspace.MaximumBytes);
    }
    [Fact]
    public async Task EditsPersistBetweenInvocationsWithoutChangingSourceAndSaveKeepsUndo()
    {
        var path = await Project(); var original = await File.ReadAllBytesAsync(path);
        Assert.Equal(0, (await Run("edit", "replace", "--project", path, "--text", "new")).Exit);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal("new", (await Run("export", "--project", path)).Out);
        var history = Result((await Run("history", "list", "--project", path)).Out);
        Assert.True(history.GetProperty("dirty").GetBoolean()); Assert.True(history.GetProperty("canUndo").GetBoolean());
        Assert.Equal(4, (await Run("project", "save", "--project", path)).Exit);
        Assert.Equal(0, (await Run("project", "save", "--project", path, "--overwrite")).Exit);
        Assert.Equal("new", (await ProjectFileService.Read(path)).Document.Text);
        Assert.False(Result((await Run("history", "list", "--project", path)).Out).GetProperty("dirty").GetBoolean());
        Assert.Equal(0, (await Run("history", "undo", "--project", path)).Exit);
        Assert.Equal("abc\ndef", (await Run("edit", "show", "--project", path)).Out);
        Assert.True(Result((await Run("history", "list", "--project", path)).Out).GetProperty("dirty").GetBoolean());
        Assert.Equal(0, (await Run("history", "redo", "--project", path)).Exit);
        Assert.False(Result((await Run("history", "list", "--project", path)).Out).GetProperty("dirty").GetBoolean());
    }
    [Fact]
    public async Task UndoThenEditDropsRedoAndClearKeepsCurrentEdits()
    {
        var path = await Project();
        Assert.Equal(0, (await Run("edit", "replace", "--project", path, "--text", "one")).Exit);
        Assert.Equal(0, (await Run("edit", "replace", "--project", path, "--text", "two")).Exit);
        Assert.Equal(0, (await Run("history", "undo", "--project", path)).Exit);
        Assert.Equal(0, (await Run("edit", "replace", "--project", path, "--text", "three")).Exit);
        Assert.Equal(2, (await Run("history", "redo", "--project", path)).Exit);
        Assert.Equal(0, (await Run("history", "clear", "--project", path)).Exit);
        Assert.Equal("three", (await Run("edit", "show", "--project", path)).Out);
        Assert.Equal(2, (await Run("history", "undo", "--project", path)).Exit);
    }
    [Fact]
    public async Task ExternalChangesAndBusyWritersReturnConflictWithoutOverwrite()
    {
        var path = await Project();
        await Run("edit", "replace", "--project", path, "--text", "dirty");
        await WorkspaceService.SaveProject(path, new(4, AsciiDocument.FromText("external"), null, null, null, "snapshot"));
        var failed = await Run("project", "save", "--project", path, "--overwrite", "--json");
        Assert.Equal(4, failed.Exit); Assert.Equal("conflict", JsonDocument.Parse(failed.Error).RootElement.GetProperty("code").GetString());
        Assert.Equal("external", (await ProjectFileService.Read(path)).Document.Text);
        Assert.Equal(2, (await Run("project", "reload", "--project", path)).Exit);
        Assert.Equal(0, (await Run("project", "reload", "--project", path, "--discard-edits")).Exit);
        Assert.Equal("external", (await Run("edit", "show", "--project", path)).Out);
        using var session = await ProjectEditSession.Open(path);
        Assert.Equal(4, (await Run("edit", "insert", "--project", path, "--text", "x")).Exit);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task UnicodeSelectionsPreserveWideGlyphsAndCombiningSequences(bool rectangle)
    {
        var path = await Project("a中e\u0301🙂z\nb中e\u0301🙂z");
        Assert.Equal(2, (await Run("edit", "select", "--project", path, "--column", "2", "--end-column", "3")).Exit);
        var selected = await Run("edit", "select", "--project", path, "--column", "1", "--end-row", "1", "--end-column", "6", "--rectangle=" + rectangle.ToString().ToLowerInvariant());
        Assert.Equal(0, selected.Exit);
        Assert.Equal(rectangle ? "中e\u0301🙂\n中e\u0301🙂" : "中e\u0301🙂z\nb中e\u0301🙂", (await Run("edit", "show", "--project", path, "--selection")).Out);
        Assert.Equal(0, (await Run("edit", "replace", "--project", path, "--selection", "--text", "X")).Exit);
        Assert.Equal(rectangle ? "aXz\nbXz" : "aXz", (await Run("edit", "show", "--project", path)).Out);
    }
    [Fact]
    public async Task FindAndReplaceNeverMatchPartOfAGrapheme()
    {
        var path = await Project("e\u0301 abc abc");
        Assert.Empty(Result((await Run("edit", "find", "--project", path, "--find", "e")).Out).GetProperty("matches").EnumerateArray());
        Assert.Equal(0, (await Run("edit", "replace", "--project", path, "--find", "abc", "--text", "中", "--all")).Exit);
        Assert.Equal("e\u0301 中 中", (await Run("edit", "show", "--project", path)).Out);
        Assert.Equal(2, (await Run("edit", "replace", "--project", path, "--text", "oops", "--all")).Exit);
        Assert.Throws<CliUsageException>(() => TextEditOperations.Find(new string('a', 1001), "a", false));
    }
    [Fact]
    public async Task InsertDeleteTransformAndNoOpUsePersistentHistory()
    {
        var path = await Project("abc");
        Assert.Equal(0, (await Run("edit", "insert", "--project", path, "--column", "1", "--text", "中")).Exit);
        Assert.Equal("a中bc", (await Run("edit", "show", "--project", path)).Out);
        Assert.Equal(0, (await Run("edit", "transform", "--project", path, "--operation", "upper")).Exit);
        Assert.Equal("A中BC", (await Run("edit", "show", "--project", path)).Out);
        await Run("edit", "replace", "--project", path, "--text", "A中BC");
        Assert.Equal(3, Result((await Run("history", "list", "--project", path)).Out).GetProperty("count").GetInt32());
        Assert.Equal(0, (await Run("edit", "delete", "--project", path)).Exit);
        Assert.Equal("", (await Run("edit", "show", "--project", path)).Out);
        Assert.Equal(0, (await Run("history", "undo", "--project", path)).Exit);
        Assert.Equal("A中BC", (await Run("edit", "show", "--project", path)).Out);
    }
    [Fact]
    public void HistoryTrimsByStepsAndSerializedByteBudgetButKeepsCurrent()
    {
        var revisions = Enumerable.Range(0, 120).Select(i => new EditRevision(AsciiDocument.FromText(new string('a', i + 1)), true)).ToArray();
        Assert.Equal(101, ProjectEditSession.TrimHistory(revisions).Count);
        Assert.Single(ProjectEditSession.TrimHistory(revisions, maximumBytes: 1));
        Assert.Same(revisions[^1], ProjectEditSession.TrimHistory(revisions, maximumBytes: 1)[0]);
        Assert.True(ProjectEditSession.TrimHistory(revisions, maximumBytes: 2000).Count < 101);
        Assert.Equal(64L * 1024 * 1024, ProjectEditSession.MaximumHistoryBytes);
    }
    [Fact]
    public async Task PersistedHistoryNeverKeepsMoreThanOneHundredUndoSteps()
    {
        var path = await Project("0");
        for (var i = 1; i <= 105; i++)
        { using var session = await ProjectEditSession.Open(path); await session.Edit(i.ToString(System.Globalization.CultureInfo.InvariantCulture), default); }
        using var reloaded = await ProjectEditSession.Open(path);
        Assert.Equal(101, reloaded.State.Revisions.Count); Assert.Equal(100, reloaded.State.Index);
        Assert.Equal("5", reloaded.State.Revisions[0].Document.Text); Assert.Equal("105", reloaded.Current.Document.Text);
    }
    [Fact]
    public async Task SaveCopyKeepsOriginalAndOldFormatInPlaceSaveCreatesBackup()
    {
        var path = await Project("old"); var legacy = new StudioProject(3, AsciiDocument.FromText("old"), null, null, null, "snapshot");
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(legacy));
        await Run("edit", "replace", "--project", path, "--text", "new");
        var copy = fixture.PathFor(Guid.NewGuid() + ".asciiproj");
        Assert.Equal(0, (await Run("project", "save", "--project", path, "--output", copy)).Exit);
        Assert.Equal("old", (await ProjectFileService.Read(path)).Document.Text);
        Assert.Equal("new", (await ProjectFileService.Read(copy)).Document.Text);
        Assert.Equal(0, (await Run("project", "save", "--project", path, "--overwrite")).Exit);
        Assert.Equal(3, JsonDocument.Parse(await File.ReadAllBytesAsync(path + ".bak")).RootElement.GetProperty("Version").GetInt32());
    }
    [Fact]
    public async Task WorkspaceKeepsIndependentProjectsDeduplicatesAndRestoresDirtyClose()
    {
        var manifest = fixture.PathFor(Guid.NewGuid() + ".workspace.json"); var a = await Project("a"); var b = await Project("b");
        Assert.Equal(0, (await Run("workspace", "open", "--workspace", manifest, "--project", a)).Exit);
        Assert.Equal(0, (await Run("workspace", "open", "--workspace", manifest, "--project", b)).Exit);
        Assert.Equal(0, (await Run("workspace", "open", "--workspace", manifest, "--project", a)).Exit);
        Assert.Equal(2, Result((await Run("workspace", "list", "--workspace", manifest)).Out).GetProperty("projects").GetArrayLength());
        Assert.Equal(0, (await Run("edit", "replace", "--workspace", manifest, "--text", "A")).Exit);
        Assert.Equal("A", (await Run("export", "--workspace", manifest)).Out);
        Assert.Equal("b", (await Run("export", "--project", b)).Out);
        Assert.Equal(2, (await Run("workspace", "close", "--workspace", manifest)).Exit);
        Assert.Equal(0, (await Run("workspace", "close", "--workspace", manifest, "--action", "keep")).Exit);
        Assert.Single(Result((await Run("workspace", "recovery", "--workspace", manifest)).Out).GetProperty("paths").EnumerateArray());
        Assert.Equal(0, (await Run("workspace", "restore", "--workspace", manifest)).Exit);
        Assert.Equal("A", (await Run("edit", "show", "--workspace", manifest)).Out);
        Assert.Equal(0, (await Run("workspace", "close", "--workspace", manifest, "--action", "save", "--overwrite")).Exit);
        Assert.Equal("A", (await ProjectFileService.Read(a)).Document.Text);
        Assert.Equal("b", (await Run("edit", "show", "--workspace", manifest)).Out);
        Assert.Equal(0, (await Run("workspace", "clear-recent", "--workspace", manifest)).Exit);
        Assert.Empty(Result((await Run("workspace", "recent", "--workspace", manifest)).Out).GetProperty("paths").EnumerateArray());
    }
    [Fact]
    public async Task WorkspaceNewNeverOverwritesAndRecoveryFailureIsReported()
    {
        var manifest = fixture.PathFor(Guid.NewGuid() + ".workspace.json"); var path = fixture.PathFor(Guid.NewGuid() + ".asciiproj");
        Assert.Equal(0, (await Run("workspace", "new", "--workspace", manifest, "--project", path, "--text", "new")).Exit);
        Assert.Equal(4, (await Run("workspace", "new", "--workspace", manifest, "--project", path)).Exit);
        await Run("edit", "replace", "--project", path, "--text", "dirty");
        await Run("workspace", "close", "--workspace", manifest, "--action", "keep");
        File.Delete(path);
        var restored = await Run("workspace", "restore", "--workspace", manifest);
        Assert.Equal(5, restored.Exit); Assert.Equal(1, Result(restored.Out).GetProperty("failures").GetInt32());
        Assert.Single(Result((await Run("workspace", "recovery", "--workspace", manifest)).Out).GetProperty("paths").EnumerateArray());
    }
    [Fact]
    public async Task ToolsAndCommentsApplyOnlyExplicitlyAndCanBeUndone()
    {
        var path = await Project("abc");
        Assert.Equal(0, (await Run("tools", "upper", "--project", path)).Exit);
        Assert.Equal("abc", (await Run("edit", "show", "--project", path)).Out);
        Assert.Equal(0, (await Run("tools", "upper", "--project", path, "--apply")).Exit);
        Assert.Equal("ABC", (await Run("edit", "show", "--project", path)).Out);
        Assert.Equal(0, (await Run("comment", "--project", path, "--syntax", "Python", "--apply")).Exit);
        Assert.Contains("#", (await Run("edit", "show", "--project", path)).Out);
        Assert.Equal(0, (await Run("history", "undo", "--project", path)).Exit);
        Assert.Equal("ABC", (await Run("edit", "show", "--project", path)).Out);
        Assert.Equal(2, (await Run("tools", "upper", "--text", "abc", "--apply")).Exit);
        Assert.Equal(2, (await Run("tools", "analyze", "--project", path, "--apply")).Exit);
        Assert.Equal("abc", (await ProjectFileService.Read(path)).Document.Text);
    }
    [Fact]
    public async Task ExportCannotOverwriteAProjectOrItsStateAndFailedApplyDoesNotMutate()
    {
        var path = await Project("abc");
        await Run("edit", "replace", "--project", path, "--text", "xyz");
        var state = await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path));
        Assert.Equal(2, (await Run("export", "--project", path, "--output", ProjectEditSession.StatePath(path), "--overwrite")).Exit);
        Assert.Equal(2, (await Run("export", "--project", path, "--output", path, "--overwrite")).Exit);
        Assert.Equal(2, (await Run("tools", "upper", "--project", path, "--apply", "--format", "PNG")).Exit);
        Assert.Equal(state, await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path)));
        Assert.Equal("abc", (await ProjectFileService.Read(path)).Document.Text);
    }
    [Fact]
    public async Task RecoveryKeepsArtworkAndHistoryWhenOriginalIsMissingOrReplaced()
    {
        var source = await Project("original");
        await Run("edit", "replace", "--project", source, "--text", "recovered"); File.Delete(source);
        var destination = fixture.PathFor(Guid.NewGuid() + ".asciiproj");
        var recovered = await Run("project", "recover", "--project", source, "--output", destination);
        Assert.Equal(0, recovered.Exit); Assert.False(Result(recovered.Out).GetProperty("sourcePreserved").GetBoolean());
        Assert.Equal("snapshot", (await ProjectFileService.Read(destination)).Mode);
        Assert.Equal("recovered", (await Run("edit", "show", "--project", destination)).Out);
        Assert.Equal(0, (await Run("history", "undo", "--project", destination)).Exit);
        Assert.Equal("original", (await Run("edit", "show", "--project", destination)).Out);
        Assert.Equal(4, (await Run("project", "recover", "--project", source, "--output", destination)).Exit);
        Assert.True(File.Exists(ProjectEditSession.StatePath(source))); Assert.False(File.Exists(source));
    }
    [Fact]
    public async Task MissingOpenProjectsCanCloseWithRecoveryWithoutDestroyingState()
    {
        var path = await Project(); var manifest = fixture.PathFor(Guid.NewGuid() + ".workspace.json");
        await Run("workspace", "open", "--project", path, "--workspace", manifest);
        await Run("edit", "replace", "--project", path, "--text", "dirty"); File.Delete(path);
        var state = await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path));
        var list = await Run("workspace", "list", "--workspace", manifest); Assert.Equal(0, list.Exit);
        Assert.False(Result(list.Out).GetProperty("projects")[0].GetProperty("available").GetBoolean());
        Assert.Equal(0, (await Run("workspace", "close", "--workspace", manifest, "--action", "keep")).Exit);
        Assert.Equal(state, await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path)));
        Assert.Single(Result((await Run("workspace", "recovery", "--workspace", manifest)).Out).GetProperty("paths").EnumerateArray());
    }
    [Fact]
    public async Task WorkspaceCannotCloseAProjectWhileAnotherWriterOwnsTheState()
    {
        var path = await Project(); var manifest = fixture.PathFor(Guid.NewGuid() + ".workspace.json");
        await Run("workspace", "open", "--project", path, "--workspace", manifest);
        using var session = await ProjectEditSession.Open(path);
        Assert.Equal(4, (await Run("workspace", "close", "--workspace", manifest, "--action", "keep")).Exit);
        using var workspace = await CliWorkspace.Open(manifest, default); Assert.Single(workspace.State.Projects);
    }
    [Fact]
    public async Task CorruptOrFutureStateIsRejectedWithoutChangingFiles()
    {
        var path = await Project(); var sidecar = ProjectEditSession.StatePath(path);
        await File.WriteAllTextAsync(sidecar, "{\"Schema\":99}"); var before = await File.ReadAllBytesAsync(path);
        Assert.Equal(3, (await Run("edit", "replace", "--project", path, "--text", "danger")).Exit);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Equal("{\"Schema\":99}", await File.ReadAllTextAsync(sidecar));
        var manifest = fixture.PathFor(Guid.NewGuid() + ".workspace.json");
        await File.WriteAllTextAsync(manifest, "{\"Schema\":99}");
        Assert.Equal(3, (await Run("workspace", "list", "--workspace", manifest)).Exit);
    }
}
