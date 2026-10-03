using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Figgle;
using Figgle.Fonts;
using AsciiStudio.Core;

namespace AsciiStudio.Services;

public sealed record TextFontEntry(string Id, string Name, string Source, string Digest, int Height, int Baseline, int Layout, string Comments);
public static class TextFontLibrary
{
    private static readonly string directory = Environment.GetEnvironmentVariable("ASCIISTUDIO_FONT_DIRECTORY") is { Length: > 0 } sharedFonts
        ? Path.GetFullPath(sharedFonts) : Path.Combine(WorkspaceService.DataDirectory, "fonts");
    private static readonly object gate = new();
    private static readonly Dictionary<string, string> builtins = LoadBuiltins();
    private static readonly Dictionary<(string, ArtPacking), FiggleFont> parsed = [];
    // The previous UI persisted Figgle property names, which differ from these filenames.
    private static readonly Dictionary<string, string> legacyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        {"OneRow","builtin:1row"},{"ThreeD","builtin:3-d"},{"ThreeDDiagonal","builtin:3d_diagonal"},
        {"ThreeByFive","builtin:3x5"},{"FourMax","builtin:4max"},{"FiveLineOblique","builtin:5lineoblique"},
        {"AmcRazor2","builtin:amcrazo2"},{"Caligraphy2","builtin:calgphy2"},{"EftiItalic","builtin:eftitalic"},
        {"Georgia16","builtin:georgi16"},{"ScriptSlant","builtin:slscript"},{"IsometricSmall","builtin:smisome1"},
        {"KeyboardSmall","builtin:smkeyboard"},{"PoisonSmall","builtin:smpoison"},{"ScriptSmall","builtin:smscript"},
        {"ShadowSmall","builtin:smshadow"},{"SlantSmall","builtin:smslant"},{"TengwarSmall","builtin:smtengwar"}
    };
    private static HashSet<string> favorites = LoadFavorites();
    private static readonly SemaphoreSlim favoriteGate = new(1, 1);
    public static bool IsFavorite(string id) { lock (gate) return favorites.Contains(id); }
    private static Dictionary<string, string> LoadBuiltins()
    {
        using var stream = typeof(FiggleFonts).Assembly.GetManifestResourceStream("Figgle.Fonts.zip")!;
        using var zip = new ZipArchive(stream); var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".flf", StringComparison.OrdinalIgnoreCase)))
        { using var reader = new StreamReader(entry.Open()); result[Path.GetFileNameWithoutExtension(entry.Name)] = reader.ReadToEnd(); }
        return result;
    }
    private static HashSet<string> LoadFavorites()
    {
        try { return (JsonSerializer.Deserialize<string[]>(BoundedFile.JsonBytes(BoundedFile.Read(Path.Combine(directory, "favorites.json"), 1_000_000)).Span) ?? []).Where(id => id is { Length: > 0 and <= 256 }).Take(1024).ToHashSet(); }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { return []; }
    }
    public static async Task ToggleFavorite(string id)
    {
        await favoriteGate.WaitAsync();
        try
        {
            string[] next;
            lock (gate) { if (!favorites.Remove(id)) favorites.Add(id); next = favorites.Order().ToArray(); }
            await WorkspaceService.AtomicWrite(Path.Combine(directory, "favorites.json"), JsonSerializer.SerializeToUtf8Bytes(next));
        }
        finally { favoriteGate.Release(); }
    }
    public static TextFontEntry[] Entries()
    {
        var entries = builtins.Select(p => Describe("builtin:" + p.Key, p.Key, p.Value, "内置")).ToList();
        if (Directory.Exists(directory)) foreach (var path in Directory.EnumerateFiles(directory, "*.flf").Take(256))
        {
            try
            {
                var source = ReadBounded(path); var digest = Digest(source);
                if (Path.GetFileNameWithoutExtension(path) != digest) continue;
                var namePath = Path.Combine(directory, digest + ".name.txt");
                var name = File.Exists(namePath) && new FileInfo(namePath).Length <= 512 ? File.ReadAllText(namePath) : digest[..8];
                entries.Add(Describe("user:" + digest, "导入 · " + name, source, "本地导入"));
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or FiggleException) { }
        }
        return entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    public static string ResolveId(string value, TextFontEntry[] entries) => legacyNames.TryGetValue(value, out var mapped) ? mapped
        : entries.FirstOrDefault(e => e.Id == value || e.Name.Equals(value, StringComparison.OrdinalIgnoreCase)
        || Normalize(e.Name) == Normalize(value))?.Id ?? "missing:" + value;
    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string ReadBounded(string path)
    {
        using var stream = File.OpenRead(path); if (stream.Length > 2_000_000) throw new ArgumentException("字体文件最多2MB。");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true)); return reader.ReadToEnd();
    }
    public static async Task<TextFontEntry> Import(string path)
    {
        if (!Path.GetExtension(path).Equals(".flf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请选择 FIGlet .flf 字体。");
        var source = await Task.Run(() => ReadBounded(path)); var digest = Digest(source);
        var entry = await Task.Run(() => { var item = Describe("user:" + digest, "导入 · " + digest[..8], source, "本地导入"); ValidateSource(source); FiggleFontParser.ParseString(source); return item; });
        Directory.CreateDirectory(directory);
        if (!File.Exists(Path.Combine(directory, digest + ".flf")) && Directory.EnumerateFiles(directory, "*.flf").Count() >= 256) throw new ArgumentException("本地字体库最多256款。");
        await WorkspaceService.AtomicWrite(Path.Combine(directory, digest + ".flf"), Encoding.UTF8.GetBytes(source));
        var name = new string(Path.GetFileNameWithoutExtension(path).Where(c => !char.IsControl(c)).Take(100).ToArray());
        await WorkspaceService.AtomicWrite(Path.Combine(directory, digest + ".name.txt"), Encoding.UTF8.GetBytes(name));
        return entry with { Name = "导入 · " + name };
    }
    public static bool Available(string id) => id.StartsWith("builtin:", StringComparison.Ordinal) ? builtins.ContainsKey(id[8..])
        : id.StartsWith("user:", StringComparison.Ordinal) && id.Length == 69 && id[5..].All(Uri.IsHexDigit) && File.Exists(Path.Combine(directory, id[5..] + ".flf"));
    public static string Source(string id)
    {
        if (!Available(id)) throw new ArgumentException("项目使用的字体不在本机字体库，请导入原字体或选择其他字体。");
        if (id.StartsWith("builtin:", StringComparison.Ordinal)) return builtins[id[8..]];
        var source = ReadBounded(Path.Combine(directory, id[5..] + ".flf"));
        if (Digest(source) != id[5..]) throw new ArgumentException("本地字体摘要不一致，请重新导入。"); return source;
    }
    private static string Digest(string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    private static TextFontEntry Describe(string id, string name, string source, string origin)
    {
        var lines = TextUtilities.Normalize(source).Split('\n'); var header = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (header.Length < 6 || !header[0].StartsWith("flf2a", StringComparison.Ordinal) || header[0].Length != 6
            || !int.TryParse(header[1], out var height) || height is < 1 or > 128
            || !int.TryParse(header[2], out var baseline) || baseline < 0 || baseline > height
            || !int.TryParse(header[5], out var comments) || comments is < 0 or > 1000 || comments + 1 >= lines.Length)
            throw new ArgumentException("FIGlet 字体头无效。");
        if (!int.TryParse(header[4], out var old)) throw new ArgumentException("FIGlet 排版规则无效。");
        var layout = header.Length > 7 && int.TryParse(header[7], out var full) ? full : old < 0 ? 0 : old == 0 ? 64 : 128 | old & 63;
        return new(id, name, origin, Digest(source), height, baseline, layout, string.Join('\n', lines.Skip(1).Take(comments)).Trim());
    }
    private static void ValidateSource(string source)
    {
        var metadata = Describe("", "", source, "");
        foreach (var line in TextUtilities.Normalize(source).Split('\n'))
            if (line.Length > 2048 || line.TakeWhile(c => c == ' ').Count() > 254 || (line.Length > 0 && line.TrimEnd(line[^1]).Reverse().TakeWhile(c => c == ' ').Count() > 254))
                throw new ArgumentException("字体行过长或空白超过解析器安全限制。");
        if (metadata.Layout is < 0 or > 32767) throw new ArgumentException("FIGlet 排版标记超出范围。");
    }
    public static FiggleFont Font(string id, ArtPacking packing)
    {
        lock (gate)
        {
            if (parsed.TryGetValue((id, packing), out var cached)) return cached;
            var source = Source(id);
            if (packing != ArtPacking.Default)
            {
                var index = source.IndexOf('\n'); if (index < 0) throw new ArgumentException("字体内容不完整。");
                var header = source[..index].TrimEnd('\r').Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                while (header.Count < 8) header.Add("0");
                header[4] = packing == ArtPacking.Full ? "-1" : packing == ArtPacking.Kern ? "0" : "63";
                header[7] = packing == ArtPacking.Full ? "0" : packing == ArtPacking.Kern ? "64" : "191";
                source = string.Join(' ', header) + source[index..];
            }
            var font = FiggleFontParser.ParseString(source);
            if (parsed.Count >= 64) parsed.Clear(); parsed[(id, packing)] = font; return font;
        }
    }
    public static string Render(string id, string text, TextArtOptions options, CancellationToken token = default)
    {
        options.Validate();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new ArgumentException("请输入1到2000个字符。");
        var font = Font(id, options.LetterSpacing > 0 ? ArtPacking.Full : options.Horizontal);
        if (text.Any(c => c > 127 || c != '\n' && c != '\r' && !font.Contains(c))) throw new ArgumentException("该 FIGlet 字体不支持输入中的字符；中文或 emoji 请切换系统字体模式。");
        var entry = Describe(id, id, Source(id), "");
        var widths = new Dictionary<char, int>();
        foreach (var c in text.Where(c => c is not '\r' and not '\n').Distinct())
        {
            token.ThrowIfCancellationRequested();
            widths[c] = TextUtilities.Normalize(font.Render(c.ToString())).TrimEnd('\n').Split('\n').Max(UnicodeGrid.Width);
        }
        string Draw(string line)
        {
            token.ThrowIfCancellationRequested();
            var estimate = line.Sum(c => (long)widths[c]) + (long)Math.Max(0, line.Length - 1) * options.LetterSpacing;
            if (estimate * font.Height > 4_000_000) throw new ArgumentException("字形完整宽度估算超过400万单元，请减少内容或换用较小字体。");
            if (font.Direction == FiggleTextDirection.RightToLeft) line = new string(line.Reverse().ToArray());
            if (options.LetterSpacing == 0) return font.Render(line);
            var parts = line.Select(c => TextUtilities.Normalize(font.Render(c.ToString())).Split('\n').Take(font.Height).ToArray()).ToArray();
            if (parts.Length == 0) return "";
            return string.Join('\n', Enumerable.Range(0, font.Height).Select(row => string.Join(new string(' ', options.LetterSpacing), parts.Select(p => p[row]))));
        }
        return TextArtLayout.Render(text, Draw, options, entry.Layout & 32512, token);
    }
}
