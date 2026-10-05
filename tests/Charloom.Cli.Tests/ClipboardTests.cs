using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using Charloom.Cli;
using Charloom.Core;
using Charloom.Services;
using Xunit;

namespace Charloom.Cli.Tests;

[Collection("CLI")]
public sealed class ClipboardTests(CliFixture fixture)
{
    private sealed class FakeClipboard : IClipboardService
    {
        public string Text = "";
        public byte[] Png = [];
        public Exception? Failure;
        public int Reads, Writes;
        public Task<string> ReadText(CancellationToken token) { Reads++; token.ThrowIfCancellationRequested(); if (Failure is not null) throw Failure; return Task.FromResult(Text); }
        public Task<byte[]> ReadPng(CancellationToken token) { Reads++; token.ThrowIfCancellationRequested(); if (Failure is not null) throw Failure; return Task.FromResult(Png); }
        public Task WriteText(string text, CancellationToken token) { token.ThrowIfCancellationRequested(); if (Failure is not null) throw Failure; Writes++; Text = text; return Task.CompletedTask; }
    }
    private static async Task<(int Exit, string Out, string Error)> Run(FakeClipboard clipboard, string[] args, string stdin = "", CancellationToken token = default)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await CliHost.Run(args, output, error, new StringReader(stdin), token, clipboard);
        return (exit, output.ToString(), error.ToString());
    }
    private async Task<string> Project(string text = "old")
    {
        var path = fixture.PathFor(Guid.NewGuid().ToString("N") + ".asciiproj");
        await WorkspaceService.SaveProject(path, new(4, AsciiDocument.FromText(text), null, null, null, "snapshot")); return path;
    }

    [Fact]
    public async Task ReadsPreserveUnicodeTabsAndLineEndings()
    {
        var clipboard = new FakeClipboard { Text = "中🙂\t\r\nlast\n" };
        var result = await Run(clipboard, ["clipboard", "read"]);
        Assert.Equal(0, result.Exit); Assert.Equal(clipboard.Text, result.Out);
        var path = fixture.PathFor(Guid.NewGuid() + ".txt");
        Assert.Equal(0, (await Run(clipboard, ["clipboard", "read", "--output", path])).Exit);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(clipboard.Text), await File.ReadAllBytesAsync(path));
        var json = await Run(clipboard, ["clipboard", "read", "--json"]);
        Assert.Equal(clipboard.Text, JsonDocument.Parse(json.Out).RootElement.GetProperty("result").GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("text")]
    [InlineData("stdin")]
    [InlineData("input")]
    public async Task WritesSupportExplicitTextSources(string source)
    {
        var clipboard = new FakeClipboard { Text = "original" }; const string text = "中文🙂\r\n";
        var args = new List<string> { "clipboard", "write", "--" + source };
        if (source == "text") args.Add(text);
        if (source == "input") { var path = fixture.PathFor(Guid.NewGuid() + ".txt"); await File.WriteAllTextAsync(path, text); args.Add(path); }
        var result = await Run(clipboard, args.ToArray(), text);
        Assert.Equal(0, result.Exit); Assert.Equal(text, clipboard.Text); Assert.Empty(result.Out); Assert.Equal(1, clipboard.Writes);
    }

    [Fact]
    public async Task CopyUsesCurrentEditWithoutSavingOrAcceptingCandidate()
    {
        var clipboard = new FakeClipboard(); var path = await Project(); var source = await File.ReadAllBytesAsync(path);
        await CliHost.Run(["edit", "replace", "--project", path, "--text", "edited"], TextWriter.Null, TextWriter.Null, TextReader.Null);
        using (var session = await ProjectEditSession.Open(path, default))
            await session.SetCandidate(AsciiDocument.FromText("candidate"), false, default);
        var state = await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path));
        Assert.Equal(0, (await Run(clipboard, ["clipboard", "write", "--project", path])).Exit);
        Assert.Equal("edited", clipboard.Text); Assert.Equal(source, await File.ReadAllBytesAsync(path));
        Assert.Equal(state, await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path)));
    }

    [Fact]
    public async Task PasteRequiresApplyAndRemainsUndoable()
    {
        var clipboard = new FakeClipboard { Text = "new 中" }; var path = await Project(); var source = await File.ReadAllBytesAsync(path);
        Assert.Equal(2, (await Run(clipboard, ["clipboard", "paste", "--project", path])).Exit); Assert.Equal(0, clipboard.Reads);
        Assert.False(File.Exists(ProjectEditSession.StatePath(path)));
        Assert.Equal(0, (await Run(clipboard, ["clipboard", "paste", "--project", path, "--apply"])).Exit);
        Assert.Equal("new 中", (await ProjectEditSession.Read(path, default)).Document.Text);
        Assert.Equal(source, await File.ReadAllBytesAsync(path));
        Assert.Equal(0, await CliHost.Run(["history", "undo", "--project", path], TextWriter.Null, TextWriter.Null, TextReader.Null));
        Assert.Equal("old", (await ProjectEditSession.Read(path, default)).Document.Text);
        Assert.Equal(0, await CliHost.Run(["history", "redo", "--project", path], TextWriter.Null, TextWriter.Null, TextReader.Null));
        Assert.Equal("new 中", (await ProjectEditSession.Read(path, default)).Document.Text);
    }

    [Fact]
    public async Task PasteHonorsUnicodeSelectionAndWorkspaceActiveProject()
    {
        var clipboard = new FakeClipboard { Text = "X" }; var path = await Project("a中b");
        await CliHost.Run(["workspace", "open", "--project", path], TextWriter.Null, TextWriter.Null, TextReader.Null);
        await CliHost.Run(["edit", "select", "--project", path, "--column", "1", "--end-column", "3"], TextWriter.Null, TextWriter.Null, TextReader.Null);
        Assert.Equal(0, (await Run(clipboard, ["clipboard", "paste", "--apply", "--selection"])).Exit);
        Assert.Equal("aXb", (await ProjectEditSession.Read(path, default)).Document.Text);
    }

    [Fact]
    public async Task ImageReadsRequireOutputAndNeverOverwriteImplicitly()
    {
        using var bitmap = new Bitmap(2, 2); using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        var clipboard = new FakeClipboard { Png = stream.ToArray() };
        Assert.Equal(2, (await Run(clipboard, ["clipboard", "read", "--format", "PNG"])).Exit); Assert.Equal(0, clipboard.Reads);
        var path = fixture.PathFor(Guid.NewGuid() + ".png");
        Assert.Equal(0, (await Run(clipboard, ["clipboard", "read", "--format", "PNG", "--output", path])).Exit);
        Assert.Equal(clipboard.Png, await File.ReadAllBytesAsync(path)); var reads = clipboard.Reads;
        Assert.Equal(4, (await Run(clipboard, ["clipboard", "read", "--format", "PNG", "--output", path])).Exit);
        Assert.Equal(reads, clipboard.Reads);
        Assert.Equal(0, (await Run(clipboard, ["clipboard", "read", "--format", "PNG", "--output", path, "--overwrite"])).Exit);
        WindowsClipboardService.ValidatePng(clipboard.Png);
    }

    [Theory]
    [InlineData("missing", 3, "clipboard_format")]
    [InlineData("busy", 4, "clipboard_busy")]
    [InlineData("cancel", 130, "canceled")]
    public async Task FailuresHaveStableCodesAndPreserveEdits(string failure, int exit, string code)
    {
        var clipboard = new FakeClipboard { Failure = failure switch { "busy" => new ClipboardBusyException(), "cancel" => new OperationCanceledException(), _ => new ClipboardContentException("No text.") } };
        var path = await Project(); var source = await File.ReadAllBytesAsync(path);
        await CliHost.Run(["edit", "replace", "--project", path, "--text", "keep"], TextWriter.Null, TextWriter.Null, TextReader.Null);
        var sidecar = await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path));
        var result = await Run(clipboard, ["clipboard", "paste", "--project", path, "--apply", "--json"]);
        Assert.Equal(exit, result.Exit); Assert.Empty(result.Out);
        Assert.Equal(code, JsonDocument.Parse(result.Error).RootElement.GetProperty("code").GetString());
        Assert.Equal(sidecar, await File.ReadAllBytesAsync(ProjectEditSession.StatePath(path))); Assert.Equal(source, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("multiple")]
    [InlineData("nul")]
    public async Task InvalidWritesDoNotTouchClipboard(string kind)
    {
        var clipboard = new FakeClipboard { Text = "private" };
        var args = kind switch { "none" => new[] { "clipboard", "write" }, "multiple" => ["clipboard", "write", "--text", "x", "--stdin"], _ => ["clipboard", "write", "--text", "a\0b"] };
        Assert.NotEqual(0, (await Run(clipboard, args)).Exit); Assert.Equal(0, clipboard.Writes); Assert.Equal("private", clipboard.Text);
    }

    [Fact]
    public void PureValidationEnforcesByteBudgetAndImageFormat()
    {
        Assert.Throws<ClipboardContentException>(() => WindowsClipboardService.ValidateText(new string('中', WindowsClipboardService.MaximumTextBytes / 3 + 1)));
        Assert.Throws<ClipboardContentException>(() => WindowsClipboardService.ValidatePng([1, 2, 3]));
        Assert.Throws<System.Text.EncoderFallbackException>(() => WindowsClipboardService.ValidateText("\uD800"));
        WindowsClipboardService.ValidateText("");
        byte[] oversizedHeader = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 5, 245, 225, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Throws<ClipboardContentException>(() => WindowsClipboardService.ValidatePng(oversizedHeader));
    }

    [Fact]
    public async Task CancellationBeforeInvocationNeverReadsClipboard()
    {
        var clipboard = new FakeClipboard(); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Equal(130, (await Run(clipboard, ["clipboard", "read"], token: canceled.Token)).Exit);
        Assert.Equal(0, clipboard.Reads);
    }

    [Fact]
    public async Task OutputProtectionAndMissingSelectionRunBeforeReadingClipboard()
    {
        var clipboard = new FakeClipboard { Text = "x" }; var path = await Project();
        Assert.Equal(2, (await Run(clipboard, ["clipboard", "paste", "--project", path, "--apply", "--selection"])).Exit);
        Assert.Equal(2, (await Run(clipboard, ["clipboard", "read", "--output", CliWorkspace.DefaultPath, "--overwrite"])).Exit);
        Assert.Equal(0, clipboard.Reads);
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public async Task EveryClipboardCommandHasHelpWithoutAccessingSystemClipboard(string language)
    {
        foreach (var command in new[] { "read", "write", "paste" })
        {
            var clipboard = new FakeClipboard(); var result = await Run(clipboard, ["clipboard", command, "--help", "--language", language]);
            Assert.Equal(0, result.Exit); Assert.Contains("clipboard " + command, result.Out); Assert.Contains("--json", result.Out);
            Assert.Equal(0, clipboard.Reads + clipboard.Writes);
        }
    }
}
