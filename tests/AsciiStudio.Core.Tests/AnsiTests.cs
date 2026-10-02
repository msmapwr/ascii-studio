using AsciiStudio.Core;
using Xunit;

namespace AsciiStudio.Core.Tests;

public sealed class AnsiTests
{
    [Fact(DisplayName = "ANSI color palette true color and iCE")]
    public void ANSIColorPaletteTrueColorAndICE()
    {
        var parsed = AnsiArt.Parse("\x1b[1;31;44mA\x1b[38;5;214;48;2;1;2;3mB\x1b[0;5;41mC", 20, true).Document;
        Assert.True(parsed.Colors![0] == 0xFFFF5555 && parsed.BackgroundColors![0] == 0xFF0000AA, "16 color palette incorrect");
        Assert.True(parsed.Colors![1] == 0xFFFFAF00 && parsed.BackgroundColors![1] == 0xFF010203, "256 or true color incorrect");
        Assert.True(parsed.BackgroundColors![2] == 0xFFFF5555, "iCE bright background incorrect");
        var inverse = AnsiArt.Parse("\x1b[31;44;7mX\x1b[27;8mY", 20).Document;
        Assert.True(inverse.Colors![0] == 0xFF0000AA && inverse.BackgroundColors![0] == 0xFFAA0000 && inverse.Colors[1] == inverse.BackgroundColors[1], "Reverse or conceal failed");
    }

    [Fact(DisplayName = "ANSI cursor erase delayed wrap and saved cursor")]
    public void ANSICursorEraseDelayedWrapAndSavedCursor()
    {
        var screen = AnsiArt.Parse("abcdef\rXY\x1b[3G\x1b[KZ\x1b[s\x1b[2;4HQ\x1b[uR", 20).Document;
        Assert.True(screen.Text.Split('\n')[0].StartsWith("XYZR") && screen.Text.Split('\n')[1][3] == 'Q', "Cursor state failed");
        Assert.True(AnsiArt.Parse(new string('a', 20) + "\r\nb", 20).Document.Height == 2, "Full line introduced blank row");
        Assert.True(AnsiArt.Parse(new string('a', 20) + "b", 20).Document.Height == 2, "Long line did not wrap");
        var erased = AnsiArt.Parse("abc\x1b[2J", 20).Document;
        Assert.True(erased.Text.Trim().Length == 0, "Erase display failed");
    }

    [Fact(DisplayName = "ANSI safe unknown sequences truncated input and resource limits")]
    public void ANSISafeUnknownSequencesTruncatedInputAndResourceLimits()
    {
        var parsed = AnsiArt.Parse("A\x1b]8;;https://example.com\aB\x1b[?25lC\x1b[", 20);
        Assert.True(parsed.Document.Text.Trim() == "ABC" && parsed.IgnoredSequences == 2 && parsed.IncompleteSequences == 1, "Unknown control string leaked");
        try { AnsiArt.Parse("\x1b[2001;1Hx", 20); throw new Exception("Unbounded cursor accepted"); } catch (ArgumentException) { }
        try { AnsiArt.Decode(new byte[AnsiArt.InputLimit + 1]); throw new Exception("Oversize file accepted"); } catch (ArgumentException) { }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { AnsiArt.Parse("abc", cancellation: cancellation.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
    }

    [Fact(DisplayName = "ANSI CP437 UTF8 SAUCE and malformed comments")]
    public void ANSICP437UTF8SAUCEAndMalformedComments()
    {
        Assert.True(AnsiArt.Decode([0xDA, 0xC4, 0xBF]).Text == "┌─┐", "CP437 box drawing failed");
        Assert.True(AnsiArt.Decode(System.Text.Encoding.UTF8.GetBytes("测试")).Text == "测试", "UTF8 failed");
        var body = System.Text.Encoding.ASCII.GetBytes("abc\x1a"); var bytes = new byte[body.Length + 128]; body.CopyTo(bytes, 0);
        System.Text.Encoding.ASCII.GetBytes("SAUCE00").CopyTo(bytes, body.Length);
        System.Text.Encoding.ASCII.GetBytes("Title").CopyTo(bytes, body.Length + 7);
        bytes[body.Length + 94] = 1; bytes[body.Length + 96] = 80; bytes[body.Length + 104] = 255;
        var source = AnsiArt.Decode(bytes);
        Assert.True(source.Text == "abc" && source.Metadata?.Title == "Title" && source.Metadata.Width == 80 && source.MetadataWarnings == 1, "SAUCE metadata separation failed");
        Assert.True(AnsiArt.Decode(System.Text.Encoding.ASCII.GetBytes("abcSAUCE00")).Text == "abcSAUCE00", "Short record crashed");
    }

    [Fact(DisplayName = "ANSI background export and document JSON compatibility")]
    public void ANSIBackgroundExportAndDocumentJSONCompatibility()
    {
        var original = AnsiArt.Parse("\x1b[31;44mABC", 20).Document;
        var restored = AnsiArt.Parse(ExportService.Ansi(original), 20).Document;
        Assert.True(original.Text == restored.Text && original.Colors!.SequenceEqual(restored.Colors!) && original.BackgroundColors!.SequenceEqual(restored.BackgroundColors!), "ANSI round trip lost colors");
        Assert.True(ExportService.Html(original).Contains("background-color:#0000AA") && ExportService.Svg(original).Contains("fill=\"#0000AA\""), "Background export missing");
        var json = System.Text.Json.JsonSerializer.Deserialize<AsciiDocument>(ExportService.Json(original))!;
        Assert.True(json.BackgroundColors!.SequenceEqual(original.BackgroundColors!), "JSON lost backgrounds");
        Assert.True(AsciiDocument.FromText("abc").BackgroundColors is null, "Legacy document background changed");
        try { (original with { BackgroundColors = [0] }).Validate(); throw new Exception("Invalid background grid accepted"); } catch (ArgumentException) { }
    }
}
