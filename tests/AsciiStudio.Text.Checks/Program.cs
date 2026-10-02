using AsciiStudio.Core;
using AsciiStudio.Services;

var directory = Path.Combine(Path.GetTempPath(), "AsciiStudio-text-checks-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("ASCIISTUDIO_DATA_DIRECTORY", directory);
var passed = 0;
void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
async Task Check(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + name); }
Directory.CreateDirectory(directory);
var fixture = "flf2a$ 1 1 5 0 1 0 64 0\nSynthetic font for checks\n" +
    string.Join('\n', Enumerable.Range(32, 95).Concat(new[] { 196, 214, 220, 228, 246, 252, 223 }).Select(c => " " + (c == 32 ? "$" : c == 65 ? "A" : "X") + " @@")) + "\n";
var path = Path.Combine(directory, "fixture.flf");
await File.WriteAllTextAsync(path, fixture);
await Check("font import validates content and deduplicates", async () =>
{
    var first = await TextFontLibrary.Import(path); var second = await TextFontLibrary.Import(path);
    Assert(first.Id == second.Id && TextFontLibrary.Available(first.Id), "Import identity changed");
    Assert(Directory.GetFiles(Path.Combine(directory, "fonts"), "*.flf").Length == 1, "Duplicate import persisted");
});
var imported = TextFontLibrary.Entries().Single(e => e.Source == "本地导入");
await Check("font packing truly changes horizontal layout", () =>
{
    string Draw(ArtPacking packing) => TextFontLibrary.Render(imported.Id, "AA", new() { Horizontal = packing, Trim = false });
    var full = Draw(ArtPacking.Full); var kern = Draw(ArtPacking.Kern); var smush = Draw(ArtPacking.Smush);
    Assert(full.Length > kern.Length && kern.Length > smush.Length, "Packing override failed");
    Assert(TextFontLibrary.Render(imported.Id, "AA", new() { LetterSpacing = 2, Trim = false }).Length > full.Length, "Explicit spacing failed");
    return Task.CompletedTask;
});
await Check("favorites survive serialized storage", async () =>
{
    await TextFontLibrary.ToggleFavorite(imported.Id); Assert(TextFontLibrary.IsFavorite(imported.Id), "Favorite missing");
    Assert(File.ReadAllText(Path.Combine(directory, "fonts", "favorites.json")).Contains(imported.Id), "Favorite not saved");
    await TextFontLibrary.ToggleFavorite(imported.Id); Assert(!TextFontLibrary.IsFavorite(imported.Id), "Favorite not removed");
});
await Check("font malformed and huge imports rejected", async () =>
{
    var invalid = Path.Combine(directory, "invalid.flf"); await File.WriteAllTextAsync(invalid, "not a font");
    try { await TextFontLibrary.Import(invalid); throw new Exception("Invalid font accepted"); } catch (ArgumentException) { }
    await File.WriteAllTextAsync(invalid, new string('x', 2_000_001));
    try { await TextFontLibrary.Import(invalid); throw new Exception("Huge font accepted"); } catch (ArgumentException) { }
});
await Check("imported digest detects changed content and missing file", async () =>
{
    var saved = Path.Combine(directory, "fonts", imported.Digest + ".flf"); await File.WriteAllTextAsync(saved, fixture + "changed");
    try { TextFontLibrary.Source(imported.Id); throw new Exception("Changed font accepted"); } catch (ArgumentException) { }
    File.Delete(saved); Assert(!TextFontLibrary.Available(imported.Id), "Missing font considered available");
});
await Check("all built-in fonts render bounded previews", () =>
{
    var entries = TextFontLibrary.Entries(); Assert(entries.Length >= 250, "Built-in catalog incomplete");
    foreach (var entry in entries) { var output = TextFontLibrary.Render(entry.Id, "abc", new()); Assert(output.Length < 100_000, "Unbounded preview"); TextFontLibrary.Render(entry.Id, "a b", new() { LetterSpacing = 1 }); }
    return Task.CompletedTask;
});
await Check("wide imported glyphs are rejected before large allocation", async () =>
{
    var wide = "flf2a$ 128 100 2048 0 1 0 64 0\nWide synthetic test font\n";
    foreach (var c in Enumerable.Range(32, 95).Concat(new[] { 196, 214, 220, 228, 246, 252, 223 }))
        wide += string.Concat(Enumerable.Range(0, 128).Select(row => (c == 65 ? new string('X', 2046) : "X") + "@@\n"));
    var file = Path.Combine(directory, "wide.flf"); await File.WriteAllTextAsync(file, wide); var entry = await TextFontLibrary.Import(file);
    try { TextFontLibrary.Render(entry.Id, new string('A', 2000), new()); throw new Exception("Huge render accepted"); } catch (ArgumentException) { }
});
await Check("every legacy built-in font name keeps the original glyphs", () =>
{
    var entries = TextFontLibrary.Entries();
    foreach (var property in typeof(Figgle.Fonts.FiggleFonts).GetProperties().Where(p => p.PropertyType == typeof(Figgle.FiggleFont)))
    {
        var id = TextFontLibrary.ResolveId(property.Name, entries); Assert(TextFontLibrary.Available(id), "Missing alias: " + property.Name);
        var legacy = (Figgle.FiggleFont)property.GetValue(null)!;
        Assert(legacy.Render("abc") == TextFontLibrary.Font(id, ArtPacking.Default).Render("abc"), "Wrong alias: " + property.Name);
    }
    return Task.CompletedTask;
});
await Check("Chinese raster weight outline fill and missing glyphs", () =>
{
    var family = FontCatalog.Names.FirstOrDefault(n => n == "Microsoft YaHei UI") ?? FontCatalog.Names.First();
    var normal = TextRasterService.Render("测试abc", new(), new(family), .5);
    var outline = TextRasterService.Render("测试abc", new(), new(family, Stroke: 2, Filled: false), .5);
    Assert(normal != outline, "Outline did not change output");
    var light = TextRasterService.Render("测试abc", new(), new(family, Bold: false), .5); Assert(light != normal, "Weight did not change output");
    Assert(TextRasterService.Missing("\u0378", family, true).Length > 0, "Undefined glyph not reported");
    return Task.CompletedTask;
});
await Check("text source parameters round trip project", async () =>
{
    var doc = AsciiDocument.FromText("result"); var parameters = new Dictionary<string, string> { { "layout", System.Text.Json.JsonSerializer.Serialize(new TextArtOptions { MaximumWidth = 80, Wrap = true, Title = "测试" }) }, { "font", "builtin:standard" } };
    var file = Path.Combine(directory, "roundtrip.asciiproj"); await WorkspaceService.SaveProject(file, new(4, doc, null, null, "测试", "text", parameters));
    var restored = await WorkspaceService.OpenProject(file); Assert(restored.SourceText == "测试" && restored.Parameters!["layout"] == parameters["layout"], "Source parameters lost");
});
Console.WriteLine($"{passed} Windows text checks passed.");
// Only this freshly generated temporary directory belongs to this process.
if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Temporary directory escaped its root");
Directory.Delete(directory, true);
