using System.Security.Cryptography;
using System.Text.Json;
using Charloom.Cli;
using Charloom.Core;
using Charloom.Services;
using Xunit;

namespace Charloom.Cli.Tests;

[Collection("CLI")]
public sealed class CandidateTests(CliFixture fixture)
{
    private static async Task<(int Exit, string Out)> Run(params string[] args)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        return (await CliHost.Run(args, output, error, new StringReader("")), output.ToString());
    }
    private string Unique(string suffix) => fixture.PathFor(Guid.NewGuid().ToString("N") + suffix);
    private async Task<string> Project()
    {
        var path = Unique(".asciiproj"); Assert.Equal(0, (await Run("text", "--text", "abc", "--save-project", path)).Exit); return path;
    }
    [Fact]
    public async Task CreatingAndReplacingCandidatesProtectsManualEditsAndSource()
    {
        var path = await Project(); var original = await File.ReadAllBytesAsync(path);
        await Run("edit", "replace", "--project", path, "--text", "manual");
        Assert.Equal(0, (await Run("candidate", "create", "--project", path)).Exit);
        var state = await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path));
        Assert.Equal(4, (await Run("candidate", "create", "--project", path)).Exit);
        Assert.Equal(state, await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path)));
        Assert.Equal(0, (await Run("candidate", "create", "--project", path, "--replace-candidate")).Exit);
        using var session = await ProjectEditSession.Open(path);
        Assert.True(session.Dirty); Assert.Equal("manual", session.Current.Document.Text); Assert.Equal(2, session.State.Revisions.Count);
        Assert.Equal((await ProjectFileService.Read(path)).Document.Text, session.State.Candidate!.Text);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }
    [Fact]
    public async Task AcceptSaveUndoRedoRestoresEditsAndGeneratedBaseline()
    {
        var path = await Project(); var original = await ProjectFileService.Read(path);
        await Run("edit", "replace", "--project", path, "--text", "manual"); await Run("candidate", "create", "--project", path);
        Assert.Equal(0, (await Run("candidate", "accept", "--project", path)).Exit);
        using (var session = await ProjectEditSession.Open(path))
        { Assert.Null(session.State.Candidate); Assert.True(session.Dirty); Assert.False(session.Current.Edited); }
        Assert.Equal(0, (await Run("project", "save", "--project", path, "--overwrite")).Exit);
        await Run("history", "undo", "--project", path);
        using (var session = await ProjectEditSession.Open(path))
        { Assert.Equal("manual", session.Current.Document.Text); Assert.True(session.Current.Edited); Assert.True(session.Dirty); Assert.Equal(original.Document.Text, session.Current.GeneratedDocument!.Text); }
        await Run("history", "redo", "--project", path);
        using var restored = await ProjectEditSession.Open(path); Assert.False(restored.Dirty); Assert.False(restored.Current.Edited);
    }
    [Fact]
    public async Task GeneratedBaselineAndColorsSurviveManualEditsUndoAndRecovery()
    {
        var path = await Project(); var original = await ProjectFileService.Read(path);
        var document = AsciiDocument.FromText("new generation") with { Colors = Enumerable.Repeat(0xffabcdefu, 14).ToArray() };
        using (var session = await ProjectEditSession.Open(path)) { await session.SetCandidate(document, false, default); await session.AcceptCandidate(default); }
        await Run("edit", "replace", "--project", path, "--text", "another edit");
        using (var session = await ProjectEditSession.Open(path)) Assert.Equal(document.Text, session.Current.GeneratedDocument!.Text);
        await Run("history", "undo", "--project", path);
        using (var session = await ProjectEditSession.Open(path)) Assert.Equal(document.Colors, session.Current.Document.Colors);
        await Run("history", "undo", "--project", path);
        using (var session = await ProjectEditSession.Open(path)) Assert.Equal(original.Document.Text, session.Current.GeneratedDocument!.Text);
        await Run("history", "redo", "--project", path); File.Delete(path); var recovered = Unique(".asciiproj");
        Assert.Equal(0, (await Run("project", "recover", "--project", path, "--output", recovered)).Exit);
        Assert.Equal(document.Text, (await ProjectFileService.Read(recovered)).GeneratedDocument!.Text);
    }
    [Fact]
    public async Task SaveRetainsSourceActiveProjectEditsAndCandidate()
    {
        var path = await Project(); var destination = Unique(".asciiproj"); var manifest = Unique(".workspace.json");
        await Run("workspace", "open", "--workspace", manifest, "--project", path); await Run("candidate", "create", "--workspace", manifest);
        var state = await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path));
        Assert.Equal(0, (await Run("candidate", "save", "--workspace", manifest, "--output", destination)).Exit);
        Assert.Equal(state, await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path)));
        var original = await ProjectFileService.Read(path); var saved = await ProjectFileService.Read(destination);
        Assert.Equal(original.SourceText, saved.SourceText); Assert.Equal(original.Mode, saved.Mode); Assert.Equal(original.Parameters, saved.Parameters); Assert.False(saved.Edited);
        using var workspace = await CliWorkspace.Open(manifest, default); Assert.Equal(path, workspace.State.Active);
        Assert.Equal(4, (await Run("candidate", "save", "--project", path, "--output", destination)).Exit);
    }
    [Fact]
    public async Task DiscardRetainsSelectionAndHistoryAndMissingActionsFail()
    {
        var path = await Project(); await Run("edit", "replace", "--project", path, "--text", "manual");
        await Run("candidate", "create", "--project", path); await Run("edit", "select", "--project", path, "--end-column", "2");
        Assert.Equal(0, (await Run("candidate", "discard", "--project", path)).Exit);
        using (var session = await ProjectEditSession.Open(path))
        { Assert.Null(session.State.Candidate); Assert.NotNull(session.State.Selection); Assert.Equal(2, session.State.Revisions.Count); Assert.Equal("manual", session.Current.Document.Text); }
        foreach (var action in new[] { "show", "accept", "discard" }) Assert.Equal(2, (await Run("candidate", action, "--project", path)).Exit);
    }
    [Fact]
    public async Task ActiveProjectExportsCannotOverwriteSourceStateOrWorkspace()
    {
        var path = await Project(); var manifest = Unique(".workspace.json");
        await Run("workspace", "open", "--workspace", manifest, "--project", path); await Run("candidate", "create", "--workspace", manifest);
        var original = await File.ReadAllBytesAsync(path); var state = await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path));
        foreach (var destination in new[] { path, ProjectEditSession.StatePath(path), manifest })
        foreach (var command in new[] { new[] { "candidate", "show" }, new[] { "candidate", "save" }, new[] { "edit", "show" } })
            Assert.Equal(2, (await Run([.. command, "--workspace", manifest, "--output", destination, "--overwrite"])).Exit);
        Assert.Equal(original, await File.ReadAllBytesAsync(path)); Assert.Equal(state, await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path)));
    }
    [Fact]
    public async Task Alpha2DigestsAndDirtyFlagsRemainCompatible()
    {
        var path = await Project(); var source = await ProjectFileService.Read(path);
        var original = new { Document = source.Document, Edited = false }; var edited = new { Document = AsciiDocument.FromText("manual"), Edited = true };
        var state = new { Schema = 1, BaseDigest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))), Revisions = new[] { original, edited }, Index = 1,
            SavedDigest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(original))), Generated = source.Document, Selection = (object?)null };
        await File.WriteAllBytesAsync(ProjectEditSession.StatePath(path), JsonSerializer.SerializeToUtf8Bytes(state));
        using (var session = await ProjectEditSession.Open(path)) { Assert.True(session.Dirty); Assert.Equal(2, session.State.Schema); }
        Assert.Equal(0, (await Run("history", "undo", "--project", path)).Exit);
        using var clean = await ProjectEditSession.Open(path); Assert.False(clean.Dirty);
    }
    [Theory]
    [InlineData("HTML")]
    [InlineData("JSON")]
    [InlineData("TXT")]
    public async Task PreviewDoesNotMutateHistory(string format)
    {
        var path = await Project(); await Run("candidate", "create", "--project", path);
        var state = await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path));
        Assert.Equal(0, (await Run("candidate", "show", "--project", path, "--format", format)).Exit);
        Assert.Equal(2, (await Run("candidate", "show", "--project", path, "--format", "PNG")).Exit);
        Assert.Equal(state, await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path)));
    }
    [Fact]
    public async Task ExternalChangesAndInvalidCandidateStateAreRejected()
    {
        var path = await Project(); await Run("candidate", "create", "--project", path); var state = await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path));
        await File.AppendAllTextAsync(path, " "); Assert.Equal(4, (await Run("candidate", "accept", "--project", path)).Exit);
        Assert.Equal(state, await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path)));
        Assert.Equal(0, (await Run("project", "reload", "--project", path, "--discard-edits")).Exit);
        var bad = JsonSerializer.Deserialize<EditState>(await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path)))!;
        await File.WriteAllBytesAsync(ProjectEditSession.StatePath(path), JsonSerializer.SerializeToUtf8Bytes(bad with { Candidate = AsciiDocument.FromText("x") with { GridVersion = 99 } }));
        Assert.Equal(3, (await Run("candidate", "status", "--project", path)).Exit);
    }
    [Theory]
    [InlineData("image")]
    [InlineData("ansi")]
    [InlineData("generator")]
    public async Task CandidatesRegenerateOtherSourceTypes(string mode)
    {
        var path = Unique(".asciiproj"); string[] args;
        if (mode == "image")
        {
            var input = Unique(".png"); using var bitmap = new System.Drawing.Bitmap(16, 16);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.Clear(System.Drawing.Color.CornflowerBlue);
            bitmap.Save(input, System.Drawing.Imaging.ImageFormat.Png);
            args = ["image", "--input", input, "--columns", "20"];
        }
        else args = mode == "ansi" ? ["ansi", "--text", "\u001b[31mABC\u001b[0m"]
            : ["generate", "--set", "Kind=2", "--set", "Width=12", "--set", "Height=8", "--set", "Seed=42"];
        Assert.Equal(0, (await Run([.. args, "--save-project", path])).Exit);
        var original = await ProjectFileService.Read(path);
        Assert.Equal(0, (await Run("candidate", "create", "--project", path)).Exit);
        using var session = await ProjectEditSession.Open(path); Assert.Equal(original.Document.Text, session.State.Candidate!.Text);
        Assert.Equal(original.Document.Colors, session.State.Candidate.Colors);
    }
    [Fact]
    public async Task RecoveryPreservesCandidateAsIndependentSnapshot()
    {
        var path = await Project(); await Run("candidate", "create", "--project", path); File.Delete(path);
        var recovered = Unique(".asciiproj"); var candidate = Unique(".asciiproj");
        Assert.Equal(0, (await Run("project", "recover", "--project", path, "--output", recovered)).Exit);
        Assert.Equal(0, (await Run("candidate", "save", "--project", recovered, "--output", candidate)).Exit);
        var saved = await ProjectFileService.Read(candidate); Assert.Equal("snapshot", saved.Mode); Assert.Null(saved.SourceText); Assert.Null(saved.SourceImage);
    }
    [Fact]
    public async Task ClosingProtectsPendingCandidateEvenWhenDocumentIsClean()
    {
        var path = await Project(); var manifest = Unique(".workspace.json");
        await Run("workspace", "open", "--project", path, "--workspace", manifest); await Run("candidate", "create", "--workspace", manifest);
        Assert.Equal(2, (await Run("workspace", "close", "--workspace", manifest)).Exit);
        Assert.Equal(0, (await Run("workspace", "close", "--workspace", manifest, "--action", "keep")).Exit);
        Assert.Equal(0, (await Run("workspace", "restore", "--workspace", manifest)).Exit);
        Assert.Equal(0, (await Run("workspace", "close", "--workspace", manifest, "--action", "save", "--overwrite")).Exit);
        using (var workspace = await CliWorkspace.Open(manifest, default)) Assert.Contains(path, workspace.State.Recovery);
        using var session = await ProjectEditSession.Open(path); Assert.NotNull(session.State.Candidate);
    }
}
