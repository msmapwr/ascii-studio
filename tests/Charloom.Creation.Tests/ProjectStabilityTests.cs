using System.Text;
using System.Text.Json;
using Charloom.Core;
using Charloom.Services;
using Xunit;

namespace Charloom.Creation.Tests;

[Collection("Workspace")]
public sealed class ProjectStabilityTests(WorkspaceTestScope scope)
{
    private static StudioProject Project() => new(1, AsciiDocument.FromText("中🙂"), new(), null, "source", "text");
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void LegacyUnicodeBomProjectsRemainReadable(bool bigEndian, bool utf32)
    {
        Encoding encoding = utf32 ? new UTF32Encoding(bigEndian, true, true) : new UnicodeEncoding(bigEndian, true, true);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(JsonSerializer.Serialize(Project()))];
        Assert.Equal("中🙂", ProjectFileService.Parse(bytes).Document.Text);
    }
    [Fact]
    public async Task BoundedReadRejectsOversizedHandleAndCancellation()
    {
        var path = scope.FilePath("bounded.bin"); await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        Assert.Throws<InvalidDataException>(() => BoundedFile.Read(path, 3));
        await Assert.ThrowsAsync<InvalidDataException>(() => BoundedFile.ReadAsync(path, 3));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await BoundedFile.ReadAsync(path, 4));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BoundedFile.ReadAsync(path, 4, cancel.Token));
    }
    [Fact]
    public async Task Utf8BomLegacyProjectMigratesAndKeepsOneBackup()
    {
        var path = scope.FilePath("legacy.asciiproj"); byte[] original = [0xEF, 0xBB, 0xBF, .. JsonSerializer.SerializeToUtf8Bytes(Project())];
        await File.WriteAllBytesAsync(path, original);
        var loaded = await WorkspaceService.OpenProject(path);
        Assert.Equal(WorkspaceService.CurrentProjectVersion, loaded.Version);
        await WorkspaceService.SaveProject(path, loaded);
        Assert.Equal(original, await File.ReadAllBytesAsync(path + ".bak"));
        await WorkspaceService.SaveProject(path, loaded with { SourceText = "changed" });
        Assert.Equal(original, await File.ReadAllBytesAsync(path + ".bak"));
    }
    [Fact]
    public async Task FutureVersionAndCorruptTargetAreNotOverwritten()
    {
        var path = scope.FilePath("future.asciiproj"); var bytes = JsonSerializer.SerializeToUtf8Bytes(Project() with { Version = 99 });
        await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkspaceService.SaveProject(path, Project()));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        await File.WriteAllTextAsync(path, "corrupt");
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkspaceService.SaveProject(path, Project()));
        Assert.Equal("corrupt", await File.ReadAllTextAsync(path));
    }
    [Fact]
    public void ProjectAndRecoveryShareStrictValidation()
    {
        var invalid = Project() with { Options = new() { Gamma = -1 } };
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectFileService.Parse(JsonSerializer.SerializeToUtf8Bytes(invalid)));
        const string id = "abcdef0123456789"; var path = WorkspaceSessionService.RecoveryPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(invalid));
        Assert.False(WorkspaceSessionService.TryLoadRecovery(id, out var project)); Assert.Null(project);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(Project()));
        Assert.True(WorkspaceSessionService.TryLoadRecovery(id, out project)); Assert.Equal(4, project!.Version);
    }
    [Fact]
    public void SessionSanitizesDuplicatesAndUnsupportedEntries()
    {
        var path = scope.FilePath("same.asciiproj");
        var first = new ProjectSessionEntry("11111111", "text", path, "first");
        var second = new ProjectSessionEntry("22222222", "text", path.ToUpperInvariant(), "second");
        var blank = new ProjectSessionEntry("33333333", "image", null, "blank");
        var invalid = new ProjectSessionEntry("../../escape", "text", null, "bad");
        var state = WorkspaceSessionService.Normalize(new([first, second, first, blank, invalid, blank with { Id = "44444444", Mode = "unknown" }], "missing", [first, blank with { Id = "55555555" }, invalid]));
        Assert.Equal(new[] { first, blank }, state.Tabs); Assert.Equal(first.Id, state.ActiveId);
        Assert.Single(state.Recoveries!); Assert.Equal("55555555", state.Recoveries![0].Id);
        Assert.Throws<ArgumentException>(() => WorkspaceSessionService.RecoveryPath("../escape"));
    }
    [Fact]
    public async Task SessionSaveAndLoadRemainBoundedAndCompatibleWithBom()
    {
        var entries = Enumerable.Range(0, 40).Select(i => new ProjectSessionEntry(i.ToString("x8"), "text", null, "tab")).ToArray();
        await WorkspaceSessionService.Save(entries, entries[2].Id, []);
        var path = Path.Combine(WorkspaceService.DataDirectory, "session.json");
        await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, .. await File.ReadAllBytesAsync(path)]);
        var loaded = WorkspaceSessionService.Load(); Assert.Equal(32, loaded.Tabs.Length); Assert.Equal(entries[2].Id, loaded.ActiveId);
    }
    [Fact]
    public void SnapshotColorsAndGeneratedOriginalAreIndependent()
    {
        var doc = AsciiDocument.FromText("A") with { Colors = [0xFF123456], BackgroundColors = [0xFFABCDEF] };
        var original = doc with { Colors = [0xFFFFFFFF] };
        var snapshot = CreationSnapshot.Capture(Project() with { Document = doc }, true, original);
        doc.Colors![0] = 0; doc.BackgroundColors![0] = 0; original.Colors![0] = 0;
        Assert.Equal(0xFF123456u, snapshot.Project.Document.Colors![0]);
        Assert.Equal(0xFFABCDEFu, snapshot.Project.Document.BackgroundColors![0]);
        Assert.Equal(0xFFFFFFFFu, snapshot.Generated!.Colors![0]);
        var same = CreationSnapshot.Capture(Project() with { Document = doc }, false, doc);
        Assert.Same(same.Project.Document, same.Generated);
    }
    [Fact]
    public void TextRestoreValidatesRasterBeforeChangingControls()
    {
        var invalid = Project() with { Parameters = new() { ["raster"] = JsonSerializer.Serialize(new TextRasterOptions("Consolas", Stroke: -1)) } };
        Assert.Throws<ArgumentException>(() => TextProjectMapper.Restore(invalid));
        Assert.Throws<ArgumentException>(() => ProjectFileService.Parse(JsonSerializer.SerializeToUtf8Bytes(invalid)));
        var restored = TextProjectMapper.Restore(Project() with { Parameters = new() { ["systemStyle"] = "1", ["border"] = "2", ["trim"] = "False" } });
        Assert.False(restored.Raster.Bold); Assert.Equal(2, restored.Layout.Border); Assert.False(restored.Layout.Trim);
    }
    [Fact]
    public void MissingFontMetadataIsRetainedForLaterRestoration()
    {
        var project = Project() with { Parameters = new() { ["mode"] = "1", ["raster"] = JsonSerializer.Serialize(new TextRasterOptions("MissingExampleFont", 240)), ["font"] = "user:missing" } };
        var restored = TextProjectMapper.Restore(project);
        Assert.Equal("MissingExampleFont", restored.Raster.Family); Assert.Equal(240, restored.Raster.Columns);
        Assert.Equal("user:missing", restored.Font); Assert.Equal(project.Document.Text, ProjectFileService.Parse(JsonSerializer.SerializeToUtf8Bytes(project)).Document.Text);
    }
    [Fact]
    public void AnsiRecoveryRejectsMalformedSourceAndKeepsOriginalBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("\x1b[31m中");
        var project = Project() with { Mode = "ansi", SourceText = "中", Parameters = new() { ["bytes"] = Convert.ToBase64String(bytes), ["encoding"] = "UTF-8", ["columns"] = "120", ["ice"] = "True" } };
        var restored = AnsiProjectMapper.Restore(project);
        Assert.Equal(bytes, restored.Bytes); Assert.Equal(120, restored.Columns); Assert.True(restored.Ice);
        Assert.Throws<FormatException>(() => AnsiProjectMapper.Restore(project with { Parameters = new() { ["bytes"] = "not base64" } }));
        Assert.Throws<ArgumentException>(() => AnsiProjectMapper.Restore(project with { Parameters = new() { ["encoding"] = "unknown" } }));
    }
    [Fact]
    public async Task ImageProjectMappingKeepsSourceAndChecksCancellation()
    {
        var controller = new ImageCreationController(); var bytes = ImagingService.Thumbnail([255, 0, 0, 255], 1, 1);
        var project = Project() with { Mode = "image", SourceImage = Convert.ToBase64String(bytes), Geometry = new() { QuarterTurns = 1 }, Parameters = new() { ["fontAspect"] = "True", ["resolution"] = "4" } };
        var restored = await controller.RestoreProject(project, default);
        Assert.Equal(bytes, restored.Source!.Bytes); Assert.True(restored.FontAspect); Assert.Equal(4, restored.Resolution); Assert.Equal(1, restored.Geometry.QuarterTurns);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.RestoreProject(project, cancel.Token));
        Assert.Throws<ArgumentException>(() => ImagingService.Thumbnail([1, 2], 10000, 10000));
    }
}
