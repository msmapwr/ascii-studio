using System.Text.Json;
using AsciiStudio.Core;
using AsciiStudio.Services;
using Xunit;

namespace AsciiStudio.Creation.Tests;

[Collection("Workspace")]
public sealed class NativeFontTests(WorkspaceTestScope scope)
{
    private const string Header = "flf2a$ 1 1 5 0 1 0 64 0\nSynthetic font for checks\n";
    private static string Fixture(string note = "") => Header.Replace("checks", "checks " + note) +
        string.Join('\n', Enumerable.Range(32, 95).Concat(new[] { 196, 214, 220, 228, 246, 252, 223 }).Select(c => " " + (c == 32 ? "$" : c == 65 ? "A" : "X") + " @@")) + "\n";
    private async Task<TextFontEntry> Import(string name)
    {
        var path = scope.FilePath(name + ".flf"); await File.WriteAllTextAsync(path, Fixture(name));
        return await TextFontLibrary.Import(path);
    }

    [Fact]
    public async Task FontImportValidatesAndDeduplicates()
    {
        var path = scope.FilePath("dedup.flf"); await File.WriteAllTextAsync(path, Fixture("dedup"));
        var first = await TextFontLibrary.Import(path); var second = await TextFontLibrary.Import(path);
        Assert.Equal(first.Id, second.Id); Assert.True(TextFontLibrary.Available(first.Id));
        Assert.Single(Directory.GetFiles(Path.Combine(scope.Directory, "fonts"), first.Digest + ".flf"));
    }
    [Fact]
    public async Task PackingAndExplicitSpacingChangeLayout()
    {
        var entry = await Import("packing");
        string Draw(ArtPacking p) => TextFontLibrary.Render(entry.Id, "AA", new() { Horizontal = p, Trim = false });
        var full = Draw(ArtPacking.Full); var kern = Draw(ArtPacking.Kern); var smush = Draw(ArtPacking.Smush);
        Assert.True(full.Length > kern.Length && kern.Length > smush.Length);
        Assert.True(TextFontLibrary.Render(entry.Id, "AA", new() { LetterSpacing = 2, Trim = false }).Length > full.Length);
    }
    [Fact]
    public async Task FavoritesArePersisted()
    {
        var entry = await Import("favorite"); await TextFontLibrary.ToggleFavorite(entry.Id);
        Assert.True(TextFontLibrary.IsFavorite(entry.Id));
        Assert.Contains(entry.Id, await File.ReadAllTextAsync(Path.Combine(scope.Directory, "fonts", "favorites.json")));
        await TextFontLibrary.ToggleFavorite(entry.Id); Assert.False(TextFontLibrary.IsFavorite(entry.Id));
    }
    [Fact]
    public async Task MalformedAndHugeImportsAreRejected()
    {
        var path = scope.FilePath("invalid.flf"); await File.WriteAllTextAsync(path, "not a font");
        await Assert.ThrowsAsync<ArgumentException>(() => TextFontLibrary.Import(path));
        await File.WriteAllTextAsync(path, new string('x', 2_000_001));
        await Assert.ThrowsAsync<ArgumentException>(() => TextFontLibrary.Import(path));
    }
    [Fact]
    public async Task DigestDetectsModifiedAndMissingFonts()
    {
        var entry = await Import("digest"); var path = Path.Combine(scope.Directory, "fonts", entry.Digest + ".flf");
        await File.WriteAllTextAsync(path, Fixture("digest") + "changed");
        Assert.Throws<ArgumentException>(() => TextFontLibrary.Source(entry.Id));
        File.Delete(path); Assert.False(TextFontLibrary.Available(entry.Id));
    }
    [Fact]
    public void BuiltinFontsHaveBoundedPreviews()
    {
        var entries = TextFontLibrary.Entries().Where(e => e.Source == "内置").ToArray();
        Assert.True(entries.Length >= 250);
        foreach (var entry in entries)
        {
            Assert.True(TextFontLibrary.Render(entry.Id, "abc", new()).Length < 100_000);
            TextFontLibrary.Render(entry.Id, "a b", new() { LetterSpacing = 1 });
        }
    }
    [Fact]
    public async Task WideGlyphsAreRejectedBeforeLargeRender()
    {
        var wide = "flf2a$ 128 100 2048 0 1 0 64 0\nWide synthetic test font\n";
        foreach (var c in Enumerable.Range(32, 95).Concat(new[] { 196, 214, 220, 228, 246, 252, 223 }))
            wide += string.Concat(Enumerable.Range(0, 128).Select(_ => (c == 65 ? new string('X', 2046) : "X") + "@@\n"));
        var path = scope.FilePath("wide.flf"); await File.WriteAllTextAsync(path, wide);
        var entry = await TextFontLibrary.Import(path);
        Assert.Throws<ArgumentException>(() => TextFontLibrary.Render(entry.Id, new string('A', 2000), new()));
    }
    [Fact]
    public void LegacyNamesKeepOriginalGlyphs()
    {
        var entries = TextFontLibrary.Entries();
        foreach (var property in typeof(Figgle.Fonts.FiggleFonts).GetProperties().Where(p => p.PropertyType == typeof(Figgle.FiggleFont)))
        {
            var id = TextFontLibrary.ResolveId(property.Name, entries); Assert.True(TextFontLibrary.Available(id));
            var legacy = (Figgle.FiggleFont)property.GetValue(null)!;
            Assert.Equal(legacy.Render("abc"), TextFontLibrary.Font(id, ArtPacking.Default).Render("abc"));
        }
    }
    [Fact]
    public void ChineseRasterWeightOutlineAndMissingGlyphs()
    {
        var family = FontCatalog.Names.FirstOrDefault(n => n == "Microsoft YaHei UI") ?? FontCatalog.Names.First();
        var normal = TextRasterService.Render("测试abc", new(), new(family), .5);
        Assert.NotEqual(normal, TextRasterService.Render("测试abc", new(), new(family, Stroke: 2, Filled: false), .5));
        Assert.NotEqual(normal, TextRasterService.Render("测试abc", new(), new(family, Bold: false), .5));
        Assert.NotEmpty(TextRasterService.Missing("\u0378", family, true));
    }
    [Fact]
    public async Task TextParametersSurviveProjectSave()
    {
        var parameters = new Dictionary<string, string> { ["layout"] = JsonSerializer.Serialize(new TextArtOptions { MaximumWidth = 80, Wrap = true, Title = "测试" }), ["font"] = "builtin:standard" };
        var path = scope.FilePath("roundtrip.asciiproj");
        await WorkspaceService.SaveProject(path, new(4, AsciiDocument.FromText("result"), null, null, "测试", "text", parameters));
        var restored = await WorkspaceService.OpenProject(path);
        Assert.Equal("测试", restored.SourceText); Assert.Equal(parameters["layout"], restored.Parameters!["layout"]);
    }
}
