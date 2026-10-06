using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using Charloom.Cli;
using Charloom.Core;
using Charloom.Services;
using Xunit;
using Xunit.Abstractions;

namespace Charloom.Cli.Tests;

[Collection("CLI")]
public sealed class GeometryEditingTests(CliFixture fixture, ITestOutputHelper diagnostics)
{
    private string Unique(string suffix) => fixture.PathFor(Guid.NewGuid().ToString("N") + suffix);
    private async Task<int> Run(params string[] args)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await CliHost.Run(args, output, error, new StringReader(""));
        if (exit != 0) diagnostics.WriteLine(error.ToString());
        return exit;
    }
    private async Task<string> Image()
    {
        var input = Unique(".png"); var path = Unique(".asciiproj");
        using var bitmap = new Bitmap(16, 8);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.CornflowerBlue);
        bitmap.Save(input, ImageFormat.Png);
        Assert.Equal(0, await Run("image", "--input", input, "--columns", "20", "--manual-aspect", "--set", "CellAspect=1", "--set", "Color=true", "--save-project", path));
        return path;
    }
    [Fact]
    public async Task DraftHistoryPersistsAndBranchesWithoutChangingArtworkOrCandidate()
    {
        var path = await Image(); var original = await File.ReadAllBytesAsync(path);
        Assert.Equal(0, await Run("geometry", "set", "--project", path, "--set", "geometry.QuarterTurns=1"));
        Assert.Equal(0, await Run("candidate", "create", "--project", path));
        Assert.Equal(0, await Run("geometry", "reset", "--project", path));
        Assert.Equal(0, await Run("geometry", "undo", "--project", path));
        using (var session = await ProjectEditSession.Open(path))
        {
            Assert.Equal(1, session.DraftGeometry.QuarterTurns); Assert.True(session.GeometryPending);
            Assert.Equal(1, session.State.CandidateGeometry!.QuarterTurns); Assert.False(session.Dirty);
            Assert.Single(session.State.Revisions); Assert.Equal(0, session.Current.Geometry!.QuarterTurns);
        }
        Assert.Equal(0, await Run("geometry", "redo", "--project", path));
        Assert.Equal(0, await Run("geometry", "undo", "--project", path));
        Assert.Equal(0, await Run("geometry", "set", "--project", path, "--set", "geometry.FlipHorizontal=true"));
        Assert.Equal(2, await Run("geometry", "redo", "--project", path));
        using (var session = await ProjectEditSession.Open(path))
        {
            // No-op assignments don't grow history; retained parameters stay immutable.
            var count = session.State.GeometryHistory!.Revisions.Count;
            await session.SetGeometry(session.DraftGeometry, default); Assert.Equal(count, session.State.GeometryHistory.Revisions.Count);
            for (var i = 0; i < 45; i++) await session.SetGeometry(new(QuarterTurns: i % 4), default);
            Assert.Equal(41, session.State.GeometryHistory.Revisions.Count);
            Assert.Equal(1, session.State.CandidateGeometry!.QuarterTurns);
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }
    [Fact]
    public async Task CandidateCapturesGeometryAndSaveUndoRedoRetainMatchingSourceParameters()
    {
        var path = await Image(); var original = await ProjectFileService.Read(path);
        Assert.Equal(0, await Run("edit", "replace", "--project", path, "--text", "manual"));
        Assert.Equal(0, await Run("geometry", "set", "--project", path, "--set", "geometry.QuarterTurns=1"));
        Assert.Equal(0, await Run("candidate", "create", "--project", path));
        Assert.Equal(0, await Run("geometry", "reset", "--project", path));
        var copy = Unique(".asciiproj");
        Assert.Equal(0, await Run("candidate", "save", "--project", path, "--output", copy));
        var candidate = await ProjectFileService.Read(copy);
        Assert.Equal(1, candidate.Geometry!.QuarterTurns); Assert.Equal(original.SourceImage, candidate.SourceImage);
        Assert.NotEqual(original.Document.Height, candidate.Document.Height); Assert.NotNull(candidate.Document.Colors);
        using (var session = await ProjectEditSession.Open(path)) Assert.Equal("manual", session.Current.Document.Text);
        Assert.Equal(0, await Run("candidate", "accept", "--project", path));
        Assert.Equal(0, await Run("project", "save", "--project", path, "--overwrite"));
        Assert.Equal(1, (await ProjectFileService.Read(path)).Geometry!.QuarterTurns);
        Assert.Equal(0, await Run("history", "undo", "--project", path));
        using (var session = await ProjectEditSession.Open(path))
        { Assert.Equal("manual", session.Current.Document.Text); Assert.Equal(0, session.Current.Geometry!.QuarterTurns); Assert.True(session.Dirty); }
        Assert.Equal(0, await Run("history", "redo", "--project", path));
        using var restored = await ProjectEditSession.Open(path);
        Assert.Equal(candidate.Document.Text, restored.Current.Document.Text); Assert.Equal(1, restored.Current.Geometry!.QuarterTurns); Assert.False(restored.Dirty);
    }
    [Fact]
    public async Task InvalidInputsAndExternalConflictsLeaveSourceAndStateUnchanged()
    {
        var path = await Image();
        Assert.Equal(0, await Run("geometry", "set", "--project", path, "--set", "geometry.Width=80"));
        var state = await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path)); var original = await File.ReadAllBytesAsync(path);
        foreach (var assignment in new[] { "geometry.Left=90", "geometry.Width=NaN", "geometry.QuarterTurns=4", "geometry.Unknown=1", "Width=10" })
            Assert.NotEqual(0, await Run("geometry", "set", "--project", path, "--set", assignment));
        Assert.Equal(state, await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path))); Assert.Equal(original, await File.ReadAllBytesAsync(path));
        var json = Unique(".json"); await File.WriteAllTextAsync(json, "{\"FlipVertical\":true}");
        Assert.Equal(0, await Run("geometry", "set", "--project", path, "--geometry", json, "--set", "geometry.FlipVertical=false"));
        using (var session = await ProjectEditSession.Open(path)) { Assert.Equal(80, session.DraftGeometry.Width); Assert.False(session.DraftGeometry.FlipVertical); }
        state = await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path));
        await File.AppendAllTextAsync(path, " ");
        Assert.Equal(4, await Run("geometry", "reset", "--project", path));
        Assert.Equal(state, await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path)));
    }
    [Fact]
    public async Task LegacyImageStateMigratesWithoutChangingCleanDigestAndRejectsCorruption()
    {
        var path = await Image(); var source = await ProjectFileService.Read(path); var revision = new EditRevision(source.Document, false);
        var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(revision)));
        var legacy = new EditState(2, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))), [revision], 0, digest, source.Document, Candidate: source.Document);
        await File.WriteAllBytesAsync(ProjectEditSession.StatePath(path), JsonSerializer.SerializeToUtf8Bytes(legacy));
        using (var session = await ProjectEditSession.Open(path))
        {
            Assert.False(session.Dirty); Assert.Equal(3, session.State.Schema); Assert.NotNull(session.State.CandidateGeometry);
            await session.SetGeometry(new(QuarterTurns: 1), default); Assert.False(session.Dirty);
        }
        var state = JsonSerializer.Deserialize<EditState>(await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path)))!;
        await File.WriteAllBytesAsync(ProjectEditSession.StatePath(path), JsonSerializer.SerializeToUtf8Bytes(state with { GeometryHistory = new([new()], 99) }));
        Assert.Equal(3, await Run("geometry", "status", "--project", path));
    }
    [Fact]
    public async Task WorkspaceProtectsDraftAndSourceLossRecoveryKeepsOnlyIndependentArtwork()
    {
        var path = await Image(); var manifest = Unique(".workspace.json");
        Assert.Equal(0, await Run("workspace", "open", "--project", path, "--workspace", manifest));
        Assert.Equal(0, await Run("geometry", "set", "--workspace", manifest, "--set", "geometry.QuarterTurns=1"));
        Assert.Equal(2, await Run("workspace", "close", "--workspace", manifest));
        Assert.Equal(0, await Run("workspace", "close", "--workspace", manifest, "--action", "save", "--overwrite"));
        using (var workspace = await CliWorkspace.Open(manifest, default)) Assert.Contains(path, workspace.State.Recovery);
        Assert.Equal(0, await Run("candidate", "create", "--project", path));
        File.Delete(path); var recovered = Unique(".asciiproj");
        Assert.Equal(0, await Run("project", "recover", "--project", path, "--output", recovered));
        using (var session = await ProjectEditSession.Open(recovered))
        { Assert.Equal("snapshot", session.Source.Mode); Assert.Null(session.Source.SourceImage); Assert.Null(session.State.GeometryHistory); Assert.NotNull(session.State.Candidate); }
        Assert.Equal(2, await Run("geometry", "reset", "--project", recovered));
    }
}
