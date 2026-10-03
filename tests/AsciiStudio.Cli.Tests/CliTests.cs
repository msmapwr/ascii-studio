using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using AsciiStudio.Cli;
using AsciiStudio.Core;
using AsciiStudio.Services;
using Xunit;

namespace AsciiStudio.Cli.Tests;

public sealed class CliFixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "AsciiStudio-cli-" + Guid.NewGuid().ToString("N"));
    private readonly string? oldData = Environment.GetEnvironmentVariable("ASCIISTUDIO_DATA_DIRECTORY");
    private readonly string? oldFonts = Environment.GetEnvironmentVariable("ASCIISTUDIO_FONT_DIRECTORY");
    public CliFixture()
    {
        Directory.CreateDirectory(DirectoryPath);
        Environment.SetEnvironmentVariable("ASCIISTUDIO_DATA_DIRECTORY", DirectoryPath);
        Environment.SetEnvironmentVariable("ASCIISTUDIO_FONT_DIRECTORY", Path.Combine(DirectoryPath, "fonts"));
    }
    public string PathFor(string name) => Path.Combine(DirectoryPath, name);
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ASCIISTUDIO_DATA_DIRECTORY", oldData);
        Environment.SetEnvironmentVariable("ASCIISTUDIO_FONT_DIRECTORY", oldFonts);
        var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(DirectoryPath).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test path.");
        Directory.Delete(DirectoryPath, true);
    }
}
[CollectionDefinition("CLI", DisableParallelization = true)]
public sealed class CliCollection : ICollectionFixture<CliFixture>;

[Collection("CLI")]
public sealed class CliTests(CliFixture fixture)
{
    private static async Task<(int Exit, string Out, string Error)> Run(string[] args, string input = "", CancellationToken token = default)
    {
        using var output = new StringWriter(); using var error = new StringWriter(); using var stdin = new StringReader(input);
        var exit = await CliHost.Run(args, output, error, stdin, token); return (exit, output.ToString(), error.ToString());
    }
    public static IEnumerable<object[]> Commands => CliCatalog.Commands.Select(c => new object[] { c.Name });
    [Theory] [MemberData(nameof(Commands))]
    public async Task EveryImplementedCommandHasCompleteBilingualHelp(string command)
    {
        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var result = await Run([.. command.Split(' '), "--help", "--language", language]);
            Assert.Equal(0, result.Exit); Assert.Empty(result.Error); Assert.Contains(command, result.Out);
            Assert.Contains("130", result.Out); Assert.Contains("--language", result.Out); Assert.Contains("--json", result.Out);
            var spec = CliCatalog.Commands.Single(c => c.Name == command);
            foreach (var option in spec.Options) Assert.Contains("--" + option.Name, result.Out);
            foreach (var model in spec.Models) foreach (var property in model.GetProperties().Where(p => p.SetMethod is not null)) Assert.Contains(property.Name, result.Out);
        }
    }
    [Theory]
    [InlineData("fonts")] [InlineData("project")] [InlineData("settings")] [InlineData("tools")] [InlineData("crypto")] [InlineData("batch")]
    public async Task CommandGroupsShowTheirSubcommands(string group)
    {
        var result = await Run([group, "--help"]); Assert.Equal(0, result.Exit);
        foreach (var command in CliCatalog.Commands.Where(c => c.Name.StartsWith(group + " ", StringComparison.Ordinal))) Assert.Contains(command.Name, result.Out);
    }
    [Theory]
    [InlineData("unknown")] [InlineData("--unknown")] [InlineData("--language", "bad")]
    [InlineData("text", "--text")] [InlineData("text", "--text", "a", "--text", "b")]
    [InlineData("fonts", "list", "--output", "bad")]
    [InlineData("image", "--manual-aspect=bad")]
    public async Task InvalidArgumentsReturnUsageWithoutStdout(params string[] args)
    {
        var result = await Run([.. args, "--json"]); Assert.Equal(2, result.Exit); Assert.Empty(result.Out);
        Assert.Equal("usage", JsonDocument.Parse(result.Error).RootElement.GetProperty("code").GetString());
    }
    [Fact]
    public void ConfigurationOverridesOnlySuppliedFieldsAndFlagsHaveHighestPriority()
    {
        var path = fixture.PathFor("partial-options.json");
        File.WriteAllText(path, "{\"contrast\":1.5}");
        var args = CliArguments.Parse(["image", "--options", path, "--set", "Gamma=2"]);
        var result = args.Model(new ConversionOptions { Columns = 80, Brightness = .7, Gamma = .5 }, fileOption: "options");
        Assert.Equal(80, result.Columns); Assert.Equal(.7, result.Brightness);
        Assert.Equal(1.5, result.Contrast); Assert.Equal(2, result.Gamma);
    }
    [Fact]
    public async Task ExplicitJsonBooleanWorksForParserErrors()
    {
        var result = await Run(["unknown", "--json=true"]);
        Assert.Equal(2, result.Exit);
        Assert.Equal("usage", JsonDocument.Parse(result.Error).RootElement.GetProperty("code").GetString());
    }
    [Fact]
    public async Task StdinPreservesRawBytesAndDoesNotAddANewline()
    {
        var result = await Run(["crypto", "apply", "--algorithm", "UTF-8", "--stdin"], "中\r\n🙂");
        Assert.Equal(0, result.Exit); Assert.Equal(Convert.ToHexString(Encoding.UTF8.GetBytes("中\r\n🙂")), result.Out); Assert.Empty(result.Error);
    }
    [Fact]
    public async Task TextUsesTheSameFigletLibraryAndLayout()
    {
        var result = await Run(["text", "--text", "abc", "--figlet-font", "Standard", "--set", "layout.LetterSpacing=2", "--set", "layout.Border=1"]);
        var id = TextFontLibrary.ResolveId("Standard", TextFontLibrary.Entries());
        Assert.Equal(0, result.Exit); Assert.Equal(TextFontLibrary.Render(id, "abc", new() { LetterSpacing = 2, Border = 1 }), result.Out);
    }
    [Fact]
    public async Task RasterUsesTheSameDesktopRenderer()
    {
        var result = await Run(["text", "--text", "测试", "--mode", "raster", "--set", "Columns=48", "--set", "Bold=false", "--set", "Stroke=2", "--set", "Filled=false"]);
        var metrics = FontCatalog.Measure("Consolas");
        Assert.Equal(0, result.Exit); Assert.Equal(TextRasterService.Render("测试", new(), new("Microsoft YaHei UI", 48, 0, false, 2, false), metrics.Width / metrics.Height), result.Out);
    }
    [Fact]
    public async Task ImageQualityAndGeometryMatchSharedController()
    {
        var path = fixture.PathFor("source.png");
        using (var bitmap = new Bitmap(12, 8))
        { using var g = Graphics.FromImage(bitmap); g.Clear(Color.RoyalBlue); g.FillRectangle(Brushes.Orange, 0, 0, 6, 8); bitmap.Save(path, ImageFormat.Png); }
        var saved = fixture.PathFor("image.asciiproj");
        var result = await Run(["image", "--input", path, "--columns", "16", "--set", "Style=HalfBlock", "--set", "Color=true", "--set", "geometry.QuarterTurns=1", "--set", "Dither=Atkinson", "--save-project", saved, "--json"]);
        Assert.Equal(0, result.Exit);
        var project = await ProjectFileService.Read(saved);
        var controller = new ImageCreationController(); var source = await controller.PrepareSource(await File.ReadAllBytesAsync(path), "Image");
        var expected = await controller.Convert(new(source.Frame.Pixels, source.Frame.Width, source.Frame.Height, source.Revision, project.Geometry!, project.Options!, project.Document.FontFamily,
            project.Document.CellWidth, project.Document.CellHeight, false, false, false, "Image"), (_, _) => Task.CompletedTask, _ => { }, default);
        Assert.Equal(expected.Document.Text, project.Document.Text); Assert.Equal(expected.Document.Colors, project.Document.Colors); Assert.Equal(expected.Document.BackgroundColors, project.Document.BackgroundColors);
    }
    [Fact]
    public async Task AnsiUsesTheSameParserAndPreservesSource()
    {
        var path = fixture.PathFor("ansi.asciiproj"); const string ansi = "\x1b[31mRED\x1b[0m\r\n中";
        var result = await Run(["ansi", "--text", ansi, "--columns", "40", "--save-project", path, "--json"]);
        Assert.Equal(0, result.Exit); var project = await ProjectFileService.Read(path); var expected = AnsiArt.Parse(ansi, 40, false);
        Assert.Equal(ansi, project.SourceText); Assert.Equal(expected.Document.Text, project.Document.Text); Assert.Equal(expected.Document.Colors, project.Document.Colors);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task AllSevenGeneratorsMatchDesktopRecipe(int kind)
    {
        var result = await Run(["generate", "--set", "Kind=" + kind, "--set", "Width=20", "--set", "Height=10"]);
        Assert.Equal(0, result.Exit); Assert.Equal(new GeneratorRecipe(Kind: kind, Width: 20, Height: 10).Generate(), result.Out);
    }
    [Theory]
    [InlineData("TXT")] [InlineData("HTML")] [InlineData("SVG")] [InlineData("ANSI")] [InlineData("JSON")] [InlineData("Markdown")]
    [InlineData("PNG")] [InlineData("JPEG")] [InlineData("GIF")]
    public async Task AllNineExportsProduceExpectedFiles(string format)
    {
        var path = fixture.PathFor("export-" + format);
        var result = await Run(["export", "--text", "中ABC", "--format", format, "--output", path]); Assert.Equal(0, result.Exit); Assert.Empty(result.Out);
        var bytes = await File.ReadAllBytesAsync(path); Assert.NotEmpty(bytes);
        if (format is "PNG" or "JPEG" or "GIF") { using var stream = new MemoryStream(bytes); using var image = Image.FromStream(stream); Assert.True(image.Width > 0); }
        if (format == "TXT") Assert.Equal("中ABC", Encoding.UTF8.GetString(bytes));
    }
    [Fact]
    public async Task OutputIsNeverSilentlyOverwritten()
    {
        var path = fixture.PathFor("existing.txt"); await File.WriteAllTextAsync(path, "original");
        var denied = await Run(["export", "--text", "new", "--output", path]); Assert.Equal(4, denied.Exit); Assert.Equal("original", await File.ReadAllTextAsync(path));
        var allowed = await Run(["export", "--text", "new", "--output", path, "--overwrite"]); Assert.Equal(0, allowed.Exit); Assert.Equal("new", await File.ReadAllTextAsync(path));
    }
    [Fact]
    public async Task UnknownPropertiesAndInvalidNamespacesAreRejected()
    {
        var typo = await Run(["image", "--input", "unused", "--set", "geometery.Left=1"]); Assert.Equal(2, typo.Exit);
        var raster = await Run(["text", "--text", "abc", "--set", "Columns=NaN"]); Assert.Equal(2, raster.Exit);
        var setting = await Run(["settings", "set", "--set", "RecentFiles=private"]); Assert.Equal(2, setting.Exit);
        var options = fixture.PathFor("invalid-options.json"); await File.WriteAllTextAsync(options, "{\"Typo\":1}");
        var badJson = await Run(["text", "--text", "abc", "--options", options]); Assert.NotEqual(0, badJson.Exit);
    }
    [Fact]
    public async Task SettingsRoundTripKeepsRecentFilesPrivateAndPreserved()
    {
        await WorkspaceService.SetSettings(new(RecentFiles: ["private.asciiproj"]));
        var set = await Run(["settings", "set", "--set", "Theme=Light", "--set", "PreviewZoom=2"]); Assert.Equal(0, set.Exit);
        var path = fixture.PathFor("preferences.json"); Assert.Equal(0, (await Run(["settings", "export", "--output", path])).Exit);
        var json = await File.ReadAllTextAsync(path); Assert.DoesNotContain("private.asciiproj", json);
        Assert.Equal(0, (await Run(["settings", "reset"])).Exit); Assert.Contains("private.asciiproj", WorkspaceService.Settings.RecentFiles!);
        Assert.Equal(0, (await Run(["settings", "import", "--input", path])).Exit); Assert.Equal("Light", WorkspaceService.Settings.Theme); Assert.Equal(2, WorkspaceService.Settings.PreviewZoom);
    }
    [Fact]
    public async Task ManualProjectIsProtectedDuringRegeneration()
    {
        var path = fixture.PathFor("edited.asciiproj"); var recipe = new GeneratorRecipe(Kind: 1);
        var project = new StudioProject(4, AsciiDocument.FromText("HAND EDIT"), null, null, recipe.Text, "generator", recipe.ToParameters(), Edited: true);
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(project));
        var denied = await Run(["project", "regenerate", "--project", path, "--save-project", path, "--overwrite"]); Assert.Equal(2, denied.Exit);
        Assert.Equal("HAND EDIT", (await ProjectFileService.Read(path)).Document.Text);
        var preview = await Run(["project", "regenerate", "--project", path]); Assert.Equal(0, preview.Exit); Assert.Equal(recipe.Generate(), preview.Out);
        var allowed = await Run(["project", "regenerate", "--project", path, "--save-project", path, "--overwrite", "--replace-edited"]); Assert.Equal(0, allowed.Exit);
    }
    [Fact]
    public async Task CryptoListIncludesEveryDesktopMethodAndAllCanBeInvoked()
    {
        var result = await Run(["crypto", "algorithms", "--json"]); Assert.Equal(0, result.Exit);
        foreach (var name in CryptoTools.Modern.Concat(CryptoTools.Digests).Concat(CryptoTools.Encodings).Concat(CryptoTools.Traditional).Concat(TextProcessing.CharacterEncodings).Concat(TextProcessing.Representations).Concat(TextProcessing.BinaryEncodings).Concat(TextProcessing.Compression).Concat(TextProcessing.Checksums))
            Assert.Contains(name, JsonDocument.Parse(result.Out).RootElement.GetProperty("result").EnumerateArray().Select(e => e.GetProperty("name").GetString()));
        var encode = await Run(["crypto", "apply", "--algorithm", "GZIP", "--text", "中🙂"]); Assert.Equal(0, encode.Exit);
        var decode = await Run(["crypto", "apply", "--algorithm", "GZIP", "--text", encode.Out, "--reverse"]); Assert.Equal(0, decode.Exit); Assert.Equal("中🙂", decode.Out);
        var password = fixture.PathFor("password.txt"); await File.WriteAllTextAsync(password, "secret\n");
        var encrypted = await Run(["crypto", "apply", "--algorithm", "AES-256-GCM", "--text", "private text", "--password-file", password]); Assert.Equal(0, encrypted.Exit); Assert.Empty(encrypted.Error);
        var decrypted = await Run(["crypto", "apply", "--algorithm", "AES-256-GCM", "--text", encrypted.Out, "--password-file", password, "--reverse"]); Assert.Equal(0, decrypted.Exit); Assert.Equal("private text", decrypted.Out);
    }
    [Theory]
    [InlineData("Python")] [InlineData("C#")] [InlineData("HTML")] [InlineData("SQL")]
    public async Task CommentWrappingUsesDesktopSyntaxAndValidation(string language)
    {
        var result = await Run(["comment", "--text", "A\nB", "--syntax", language]); Assert.Equal(0, result.Exit); Assert.Equal(CommentTools.Wrap("A\nB", language), result.Out);
        var conflict = await Run(["comment", "--text", "*/", "--syntax", "C", "--block"]); Assert.Equal(3, conflict.Exit);
    }
    [Fact]
    public async Task CanceledAndOversizedInputsFailWithoutOutput()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var canceled = await Run(["generate"], token: cancellation.Token); Assert.Equal(130, canceled.Exit); Assert.Empty(canceled.Out);
        var overflow = await Run(["tools", "upper", "--stdin"], new string('x', 8_000_001)); Assert.Equal(3, overflow.Exit); Assert.Empty(overflow.Out);
    }
    [Fact]
    public async Task BatchContinuesFailedFilesAndProducesANonzeroReport()
    {
        var directory = fixture.PathFor("batch-input"); var output = fixture.PathFor("batch-output"); Directory.CreateDirectory(directory);
        using (var bitmap = new Bitmap(8, 8)) { using var g = Graphics.FromImage(bitmap); g.Clear(Color.Orange); bitmap.Save(Path.Combine(directory, "good.png"), ImageFormat.Png); }
        await File.WriteAllTextAsync(Path.Combine(directory, "bad.png"), "invalid image");
        var result = await Run(["batch", "image", "--input", directory, "--output", output, "--columns", "8", "--json"]); Assert.Equal(5, result.Exit);
        var report = JsonDocument.Parse(result.Out).RootElement.GetProperty("result"); Assert.Equal(1, report.GetProperty("failures").GetInt32()); Assert.True(File.Exists(Path.Combine(output, "good.txt")));
    }
    [Fact]
    public async Task CapabilitiesExplicitlyReportUnfinishedDesktopParity()
    {
        var result = await Run(["capabilities", "--json"]); Assert.Equal(0, result.Exit);
        var report = JsonDocument.Parse(result.Out).RootElement.GetProperty("result"); Assert.False(report.GetProperty("desktopParityComplete").GetBoolean()); Assert.NotEmpty(report.GetProperty("pending").EnumerateArray());
    }
}
