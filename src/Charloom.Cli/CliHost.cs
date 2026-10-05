using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Charloom.Core;
using Charloom.Services;

namespace Charloom.Cli;

public static partial class CliHost
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static async Task<int> Run(string[] raw, TextWriter output, TextWriter error, TextReader input, CancellationToken token = default)
    {
        CliArguments? args = null;
        try
        {
            args = CliArguments.Parse(raw);
            if (args.Flag("help") || args.Command.Length == 0 && !args.Flag("version")) { await output.WriteAsync(CliCatalog.Help(args)); return 0; }
            if (args.Flag("version")) { await output.WriteLineAsync("1.0.0-alpha.5"); return 0; }
            token.ThrowIfCancellationRequested();
            return await new Invocation(args, output, error, input, token).Execute();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException and not StackOverflowException)
        {
            var code = failure is OperationCanceledException ? "canceled" : failure is CliConflictException ? "conflict" : failure is CliUsageException ? "usage"
                : failure is IOException or UnauthorizedAccessException ? "io" : "conversion";
            var exit = code switch { "canceled" => 130, "usage" => 2, "io" or "conflict" => 4, _ => 3 };
            var json = args?.Flag("json") ?? raw.Any(value => value is "--json" or "--json=true");
            var message = code == "canceled" ? (args is not null && CliCatalog.Chinese(args) ? "任务已取消。" : "Operation canceled.") : failure.Message;
            // No command arguments, text, passwords or private keys in diagnostics.
            await error.WriteLineAsync(json ? JsonSerializer.Serialize(new { ok = false, code, message }) : code + ": " + message);
            return exit;
        }
    }

    private sealed partial class Invocation(CliArguments args, TextWriter output, TextWriter error, TextReader input, CancellationToken token)
    {
        private CliArguments Args => args;
        private CancellationToken Token => token;
        private bool Json => args.Flag("json");
        private string? applySourceText;
        private static string Normalize(string text) => TextUtilities.Normalize(text);
        private string Family => args.Get("font", "Consolas")!;
        private string FontId(string name)
        {
            var id = TextFontLibrary.ResolveId(name, TextFontLibrary.Entries());
            if (!TextFontLibrary.Available(id)) throw new ArgumentException("FIGlet font unavailable: " + name);
            return id;
        }
        private async Task Report(object value)
        {
            token.ThrowIfCancellationRequested();
            await output.WriteLineAsync(JsonSerializer.Serialize(new { ok = true, command = args.Command, result = value }, CliArguments.Json));
        }
        public async Task<int> Execute()
        {
            if (args.Get("format") is { } selectedFormat && selectedFormat is not ("TXT" or "PNG" or "JPEG" or "GIF" or "HTML" or "SVG" or "ANSI" or "JSON" or "Markdown"))
                throw new CliUsageException("Unsupported --format. Use TXT/PNG/JPEG/GIF/HTML/SVG/ANSI/JSON/Markdown.");
            if (args.Flag("transparent") && args.Get("format") == "JPEG") throw new CliUsageException("JPEG does not support transparency.");
            if (args.Flag("apply") && (!args.Has("project") || args.Command == "comment" && args.Flag("list") || args.Command == "tools analyze"))
                throw new CliUsageException("--apply requires a project input and a text-changing operation.");
            if (args.Command.StartsWith("candidate ", StringComparison.Ordinal)) { await CandidateCommand(); return 0; }
            if (args.Command.StartsWith("edit ", StringComparison.Ordinal) || args.Command.StartsWith("history ", StringComparison.Ordinal)
                || args.Command is "project save" or "project reload" or "project recover") { await EditCommand(); return 0; }
            if (args.Command.StartsWith("workspace ", StringComparison.Ordinal)) return await WorkspaceCommand();
            if (!args.Has("project") && (args.Command.StartsWith("project ", StringComparison.Ordinal) || args.Command == "export" && !args.Has("text") && !args.Has("input") && !args.Flag("stdin")))
                args.Values["project"] = [await TargetPath()];
            switch (args.Command)
            {
                case "image": await EmitProject(await Image()); break;
                case "text": await EmitProject(await Text()); break;
                case "ansi": await EmitProject(await Ansi()); break;
                case "generate": await EmitProject(await Generator()); break;
                case "export": await Emit(await Document()); break;
                case "project info":
                {
                    var project = await LoadProject(); await Report(new { project.Version, project.Mode, project.Document.Title, project.Document.Width, project.Document.Height,
                        project.Edited, hasImage = project.SourceImage is not null, hasText = project.SourceText is not null, project.Parameters, project.Geometry }); break;
                }
                case "project validate": await LoadProject(); await Report(new { valid = true }); break;
                case "project migrate":
                {
                    var project = await LoadProject(); var destination = args.Require("output"); await Save(destination, project); await Report(new { path = Path.GetFullPath(destination), project.Version }); break;
                }
                case "project regenerate":
                {
                    var source = await LoadProject();
                    if (source.Edited && args.Has("save-project") && Path.GetFullPath(args.Require("project")).Equals(Path.GetFullPath(args.Require("save-project")), StringComparison.OrdinalIgnoreCase) && !args.Flag("replace-edited"))
                        throw new CliUsageException("Edited project protected. Use a different --save-project path or explicit --replace-edited.");
                    await EmitProject(await Regenerate(source)); break;
                }
                case "comment":
                    if (args.Flag("list")) await Report(CommentTools.Languages);
                    else await EmitText(CommentTools.Wrap(await InputText(), args.Require("syntax"), args.Flag("block")));
                    break;
                case "crypto algorithms": await Report(Algorithms()); break;
                case "crypto apply":
                {
                    if (args.Flag("password-stdin") && args.Flag("stdin")) throw new CliUsageException("Input text and password cannot both read stdin.");
                    if (args.Flag("password-stdin") && args.Has("password-file")) throw new CliUsageException("Use one password source.");
                    var text = await InputText();
                    var secret = args.Get("password-file") is { } path ? Utf8.GetString(await BoundedFile.ReadAsync(path, 16_384, token)).TrimEnd('\r', '\n')
                        : args.Flag("password-stdin") ? (await ReadStdin(16_384)).TrimEnd('\r', '\n') : "";
                    var key = args.Get("key-file") is { } keyPath ? Utf8.GetString(await BoundedFile.ReadAsync(keyPath, 16_384, token)) : "";
                    var result = await Task.Run(() => CryptoTools.Apply(args.Require("algorithm"), text, secret, args.Flag("reverse"), key,
                        new(args.Flag("base64-bytes"), args.Flag("bom"))), token);
                    token.ThrowIfCancellationRequested(); await EmitText(result); break;
                }
                case "crypto keys":
                {
                    var directory = Path.GetFullPath(args.Require("output")); var publicPath = Path.Combine(directory, "public.pem"); var privatePath = Path.Combine(directory, "private.pem");
                    EnsureWritable(publicPath); EnsureWritable(privatePath);
                    var keys = await Task.Run(CryptoTools.GenerateRsaKeys, token);
                    await Write(publicPath, Utf8.GetBytes(keys.PublicKey)); await Write(privatePath, Utf8.GetBytes(keys.PrivateKey));
                    await Report(new { publicKey = publicPath, privateKey = privatePath, bits = 3072, notice = "Private key is sensitive. Keep it private." }); break;
                }
                case "fonts list": await FontList(); break;
                case "fonts import": await Report(await TextFontLibrary.Import(args.Require("input"))); break;
                case "fonts favorite":
                {
                    var id = FontId(args.Require("figlet-font")); await TextFontLibrary.ToggleFavorite(id); await Report(new { id, favorite = TextFontLibrary.IsFavorite(id) }); break;
                }
                case "fonts sample":
                {
                    var text = args.Get("text", args.Flag("system") ? "测试" : "abc")!;
                    var sample = args.Flag("system") ? await Task.Run(() => TextRasterService.Render(text, new(), new(args.Get("system-font", "Microsoft YaHei UI")!, 48), .5, token), token)
                        : await Task.Run(() => TextFontLibrary.Render(FontId(args.Get("figlet-font", "Standard")!), text, new(), token), token);
                    await Emit(AsciiDocument.FromText(sample)); break;
                }
                case "settings show": await Report(WorkspaceService.Settings); break;
                case "settings set":
                    if (!args.Has("set")) throw new CliUsageException("Provide at least one --set Name=Value.");
                    args.ValidateAssignments(); await WorkspaceService.SetSettings(args.Model(WorkspaceService.Settings with { })); await Report(WorkspaceService.Settings); break;
                case "settings reset":
                    await WorkspaceService.SetSettings(new StudioSettings(RecentFiles: WorkspaceService.Settings.RecentFiles)); await Report(WorkspaceService.Settings); break;
                case "settings export":
                    await Write(args.Require("output"), JsonSerializer.SerializeToUtf8Bytes(WorkspaceService.Settings with { RecentFiles = [] }, CliArguments.Json));
                    await Report(new { path = Path.GetFullPath(args.Require("output")) }); break;
                case "settings import":
                {
                    var settings = JsonSerializer.Deserialize<StudioSettings>(BoundedFile.JsonBytes(await BoundedFile.ReadAsync(args.Require("input"), 1_000_000, token)).Span, CliArguments.Json)
                        ?? throw new ArgumentException("Settings cannot be null.");
                    await WorkspaceService.SetSettings(settings with { RecentFiles = WorkspaceService.Settings.RecentFiles }); await Report(WorkspaceService.Settings); break;
                }
                case "batch image": return await Batch();
                case "capabilities": await Report(new
                {
                    schema = 1, version = "1.0.0-alpha.5", status = "prerelease", commands = CliCatalog.Commands.Select(c => c.Name).ToArray(),
                    complete = new[] { "image-quality-options", "image-geometry", "figlet-layout", "system-text-raster", "ansi-sauce", "generators", "nine-export-formats", "text-tools", "all-existing-crypto-methods", "font-library", "current-settings", "bounded-image-batch", "command-help", "persistent-edit-history", "unicode-edit-selections", "workspace-tabs-and-recovery", "explicit-tool-apply", "external-change-recovery", "candidate-result-management" },
                    pending = new[] { "clipboard", "viewport-selection-and-comparison", "geometry-history", "settings-search-and-favorites", "recipes-and-platform-assistant", "code-variable-wrapping", "tutorial", "GUI-zh-CN-en-US", "new-personalization-settings", "extended-motion", "localized-domain-errors" },
                    desktopParityComplete = false, formal100Authorized = false
                }); break;
                default:
                    if (args.Command.StartsWith("tools ", StringComparison.Ordinal)) await Tools();
                    else throw new CliUsageException("Command is not implemented: " + args.Command);
                    break;
            }
            return 0;
        }

        private async Task<string> ReadStdin(int maximumBytes = 8_000_000)
        {
            var buffer = new char[4096]; var b = new StringBuilder(); int count;
            while ((count = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                if ((long)b.Length + count > maximumBytes) throw new ArgumentException("Standard input exceeds budget.");
                b.Append(buffer, 0, count);
            }
            var text = b.ToString(); if (Utf8.GetByteCount(text) > maximumBytes) throw new ArgumentException("UTF-8 input exceeds budget.");
            return text;
        }
        private async Task<string> InputText(bool optional = false)
        {
            var sources = new[] { "text", "input", "stdin", "project" }.Count(name => args.Has(name) && (name != "stdin" || args.Flag(name)));
            if (sources > 1 || !optional && sources == 0) throw new CliUsageException("Provide exactly one of --text, --input, --stdin, --project.");
            var text = args.Get("text") ?? (args.Has("project") ? (await LoadProject()).Document.Text
                : args.Has("input") ? Utf8.GetString(StripBom(await BoundedFile.ReadAsync(args.Require("input"), 8_000_000, token)))
                : args.Flag("stdin") ? await ReadStdin() : "");
            if (Utf8.GetByteCount(text) > 8_000_000) throw new ArgumentException("Text exceeds 8MB budget.");
            if (args.Flag("apply")) applySourceText = text;
            return text;
        }
        private static byte[] StripBom(byte[] bytes) => bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? bytes[3..] : bytes;
        private Task<StudioProject> LoadProject() => ProjectEditSession.Read(args.Require("project"), token);
        private AsciiDocument Font(AsciiDocument document, string? family = null)
        {
            var name = family ?? Family;
            if (!FontCatalog.Names.Contains(name)) throw new ArgumentException("System font unavailable: " + name);
            var metric = FontCatalog.Measure(name); return document with { FontFamily = name, CellWidth = metric.Width, CellHeight = metric.Height };
        }
        private async Task<AsciiDocument> Document() => args.Has("project") && !args.Has("text") && !args.Has("input") && !args.Flag("stdin")
            ? args.Has("font") ? Font((await LoadProject()).Document) : (await LoadProject()).Document
            : Font(AsciiDocument.FromText(Normalize(await InputText())));

        private async Task<StudioProject> Image(string? sourcePath = null)
        {
            args.ValidateAssignments("geometry");
            if (sourcePath is null && args.Has("input") == args.Has("project")) throw new CliUsageException("Image requires one --input or --project.");
            var saved = sourcePath is null && args.Has("project") ? await LoadProject() : null;
            if (saved is not null && saved.Mode != "image") throw new CliUsageException("Expected an image project.");
            if (saved is not null && saved.SourceImage is null) throw new ArgumentException("Image project source missing.");
            var bytes = saved?.SourceImage is { } encoded ? Convert.FromBase64String(encoded)
                : await BoundedFile.ReadAsync(sourcePath ?? args.Require("input"), 40_000_000, token);
            var options = args.Model(saved?.Options ?? new ConversionOptions(), fileOption: "options");
            if (args.Has("columns")) options = options with { Columns = args.Integer("columns", 120) };
            if (args.Has("rows")) options = options with { Rows = args.Integer("rows", 0) };
            var geometry = args.Model(saved?.Geometry ?? new ImageGeometry(), "geometry", "geometry"); geometry.Validate();
            var family = args.Get("font", saved?.Document.FontFamily ?? "Consolas")!;
            if (!FontCatalog.Names.Contains(family)) throw new ArgumentException("System font unavailable: " + family);
            var metrics = FontCatalog.Measure(family);
            if (!args.Flag("manual-aspect")) options = options with { CellAspect = metrics.Width / metrics.Height };
            var controller = new ImageCreationController();
            var sourceTitle = saved?.Document.Title ?? Path.GetFileNameWithoutExtension(sourcePath ?? args.Require("input"));
            var source = await controller.PrepareSource(bytes, sourceTitle, token: token);
            var result = await controller.Convert(new(source.Frame.Pixels, source.Frame.Width, source.Frame.Height, source.Revision, geometry,
                options, family, metrics.Width, metrics.Height, args.Flag("native-size"), false, false, source.Title), (_, _) => Task.CompletedTask, _ => { }, token);
            return controller.Project(result.Document, result.Options, source.Encoded, !args.Flag("manual-aspect"), args.Flag("native-size") ? 4 : 5, geometry);
        }
        private async Task<StudioProject> Text()
        {
            args.ValidateAssignments("layout");
            var sourceProject = args.Has("project") ? await LoadProject() : null;
            if (sourceProject is not null && sourceProject.Mode != "text") throw new CliUsageException("Expected a text project.");
            if (sourceProject is not null && (args.Has("text") || args.Has("input") || args.Flag("stdin"))) throw new CliUsageException("Text project is exclusive with other input sources.");
            var state = sourceProject is null ? null : TextProjectMapper.Restore(sourceProject);
            var text = Normalize(sourceProject?.SourceText ?? await InputText());
            var mode = args.Get("mode", state?.Mode == 1 ? "raster" : "figlet"); if (mode is not ("figlet" or "raster")) throw new CliUsageException("--mode: figlet | raster");
            var raster = args.Model(state?.Raster ?? new TextRasterOptions("Microsoft YaHei UI"), fileOption: "options");
            if (args.Has("system-font")) raster = raster with { Family = args.Require("system-font") };
            var layout = args.Model(state?.Layout ?? new TextArtOptions(), "layout", "layout"); layout.Validate(); TextRasterService.ValidateOptions(raster);
            var id = args.Get("figlet-font", state?.Font ?? "Standard")!;
            if (mode == "figlet") id = FontId(id);
            else
            {
                if (!FontCatalog.Names.Contains(raster.Family)) throw new ArgumentException("System font unavailable: " + raster.Family);
                var missing = await Task.Run(() => TextRasterService.Missing(text, raster.Family, raster.Bold), token);
                if (missing.Length > 0)
                {
                    if (!args.Flag("allow-missing")) throw new ArgumentException("Missing glyphs; use --allow-missing to continue.");
                    if (!args.Flag("quiet")) await error.WriteLineAsync(Json ? JsonSerializer.Serialize(new { warning = "missing-glyphs", count = missing.Length }) : "warning: missing glyphs");
                }
            }
            var family = args.Get("font", sourceProject?.Document.FontFamily ?? "Consolas")!;
            if (!FontCatalog.Names.Contains(family)) throw new ArgumentException("System font unavailable: " + family);
            var metrics = FontCatalog.Measure(family);
            var parameters = new Dictionary<string, string> { ["mode"] = mode == "raster" ? "1" : "0", ["font"] = id, ["systemFont"] = raster.Family,
                ["systemStyle"] = raster.Style.ToString(CultureInfo.InvariantCulture), ["raster"] = JsonSerializer.Serialize(raster), ["layout"] = JsonSerializer.Serialize(layout), ["allowMissing"] = args.Flag("allow-missing").ToString() };
            if (mode == "figlet") parameters["fontDigest"] = TextFontLibrary.Entries().First(e => e.Id == id).Digest;
            var request = new TextCreationRequest(text, mode == "raster" ? 1 : 0, id, layout, raster, family, metrics.Width, metrics.Height, parameters);
            var controller = new TextCreationController(); var document = await controller.Generate(request, token); controller.Accept(request); return controller.Project(document);
        }
        private async Task<StudioProject> Ansi()
        {
            var encoding = args.Get("encoding", "Auto")!;
            if (encoding is not ("Auto" or "UTF-8" or "CP437")) throw new CliUsageException("--encoding: Auto | UTF-8 | CP437");
            byte[]? bytes = null; AnsiSource source;
            if (args.Has("input"))
            {
                if (args.Has("text") || args.Flag("stdin")) throw new CliUsageException("Use one ANSI input source.");
                bytes = await BoundedFile.ReadAsync(args.Require("input"), AnsiArt.InputLimit, token); source = AnsiArt.Decode(bytes, encoding);
            }
            else source = new(await InputText(), "Unicode", null, 0);
            var columns = args.Integer("columns", source.Metadata?.Width is >= 20 and <= 300 ? source.Metadata.Width : 80);
            if (columns is < 20 or > 300) throw new CliUsageException("ANSI columns: 20–300.");
            var ice = args.Has("ice") ? args.Flag("ice") : source.Metadata?.IceColors ?? false;
            var parsed = await Task.Run(() => AnsiArt.Parse(source.Text, columns, ice, source.Metadata?.Title ?? "ANSI art", token), token);
            return new(WorkspaceService.CurrentProjectVersion, Font(parsed.Document), null, null, source.Text, "ansi", new()
            { ["encoding"] = encoding, ["columns"] = columns.ToString(CultureInfo.InvariantCulture), ["ice"] = ice.ToString(), ["bytes"] = bytes is null ? "" : Convert.ToBase64String(bytes) });
        }
        private async Task<StudioProject> Generator()
        {
            args.ValidateAssignments();
            var sourceProject = args.Has("project") ? await LoadProject() : null;
            if (sourceProject is not null && sourceProject.Mode != "generator") throw new CliUsageException("Expected a generator project.");
            var defaults = sourceProject is null ? new GeneratorRecipe() : GeneratorRecipe.FromParameters(sourceProject.Parameters, sourceProject.SourceText);
            var recipe = args.Model(defaults, fileOption: "options");
            if (args.Has("text") || args.Has("input") || args.Flag("stdin")) recipe = recipe with { Text = Normalize(await InputText()) };
            recipe.Validate(); var text = await Task.Run(recipe.Generate, token);
            return new(WorkspaceService.CurrentProjectVersion, Font(AsciiDocument.FromText(text, "Generator")), null, null, recipe.Text, "generator", recipe.ToParameters());
        }
        private async Task<StudioProject> Regenerate(StudioProject project)
        {
            AsciiDocument generated;
            switch (project.Mode)
            {
                case "image":
                {
                    var controller = new ImageCreationController(); var state = await controller.RestoreProject(project, token);
                    var source = state.Source ?? throw new ArgumentException("Image source missing.");
                    var result = await controller.Convert(new(source.Frame.Pixels, source.Frame.Width, source.Frame.Height, source.Revision, state.Geometry, state.Options,
                        project.Document.FontFamily, project.Document.CellWidth, project.Document.CellHeight, false, false, false, project.Document.Title), (_, _) => Task.CompletedTask, _ => { }, token);
                    generated = result.Document; break;
                }
                case "text":
                {
                    var state = TextProjectMapper.Restore(project); var controller = new TextCreationController();
                    generated = await controller.Generate(new(project.SourceText ?? "", state.Mode, state.Font, state.Layout, state.Raster, project.Document.FontFamily,
                        project.Document.CellWidth, project.Document.CellHeight, project.Parameters ?? new()), token); break;
                }
                case "ansi":
                {
                    var state = AnsiProjectMapper.Restore(project); var text = state.Bytes is null ? state.Text : AnsiArt.Decode(state.Bytes, state.Encoding).Text;
                    generated = (await Task.Run(() => AnsiArt.Parse(text, state.Columns, state.Ice, state.Title, token), token)).Document
                        with { FontFamily = project.Document.FontFamily, CellWidth = project.Document.CellWidth, CellHeight = project.Document.CellHeight }; break;
                }
                case "generator":
                    generated = Font(AsciiDocument.FromText(await Task.Run(GeneratorRecipe.FromParameters(project.Parameters, project.SourceText).Generate, token), project.Document.Title), project.Document.FontFamily); break;
                default: throw new CliUsageException("This project has no regeneratable source.");
            }
            return project with { Document = generated, Edited = false, GeneratedDocument = null };
        }

        private async Task Tools()
        {
            var text = await InputText(); var normalized = Normalize(text);
            var result = args.Command[6..] switch
            {
                "analyze" => TextUtilities.Analyze(text), "trim" => TextUtilities.TrimCanvas(text), "clean" => TextUtilities.Clean(text, false), "ascii" => TextUtilities.Clean(text, true),
                "upper" => text.ToUpperInvariant(), "lower" => text.ToLowerInvariant(), "mirror" => string.Join('\n', normalized.Split('\n').Select(line => string.Concat(TextArtLayout.Elements(line).Reverse()))),
                "flip" => string.Join('\n', normalized.Split('\n').Reverse()), "expand-tabs" => normalized.Replace("\t", "    "), _ => throw new CliUsageException("Unknown tool.")
            };
            await Emit(Font(AsciiDocument.FromText(Normalize(result), "Text tools")));
        }
        private async Task FontList()
        {
            var search = args.Get("search", "")!;
            if (args.Flag("system"))
            {
                if (args.Flag("favorites")) throw new CliUsageException("--favorites is for FIGlet fonts.");
                await Report(FontCatalog.Names.Where(name => name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    && (!args.Flag("monospace") || FontCatalog.IsMonospaced(name)) && (!args.Flag("chinese") || FontCatalog.IsChineseCommon(name))).ToArray());
            }
            else
            {
                if (args.Flag("monospace") || args.Flag("chinese")) throw new CliUsageException("System filters require --system.");
                await Report(TextFontLibrary.Entries().Where(e => e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    && (!args.Flag("favorites") || TextFontLibrary.IsFavorite(e.Id))).Select(e => new { font = e, favorite = TextFontLibrary.IsFavorite(e.Id) }).ToArray());
            }
        }
        private static object[] Algorithms() => CryptoTools.Modern.Concat(CryptoTools.Digests).Concat(CryptoTools.Encodings).Concat(CryptoTools.Traditional)
            .Concat(TextProcessing.CharacterEncodings).Concat(TextProcessing.Representations).Concat(TextProcessing.BinaryEncodings).Concat(TextProcessing.Compression).Concat(TextProcessing.Checksums)
            .Distinct().Select(name => (object)new { name, supported = CryptoTools.IsSupported(name), reversible = !CryptoTools.Digests.Contains(name) && (!TextProcessing.Contains(name) || TextProcessing.CanReverse(name)) }).ToArray();

        private void EnsureWritable(string path, bool projectSave = false)
        {
            var full = Path.GetFullPath(path);
            var manifest = Path.GetFullPath(args.Get("workspace", CliWorkspace.DefaultPath)!);
            if (full.Equals(manifest, StringComparison.OrdinalIgnoreCase) || full.Equals(manifest + ".lock", StringComparison.OrdinalIgnoreCase)
                || args.Get("project") is { } source && (full.Equals(ProjectEditSession.StatePath(source), StringComparison.OrdinalIgnoreCase) || full.Equals(ProjectEditSession.StatePath(source) + ".lock", StringComparison.OrdinalIgnoreCase)
                    || !projectSave && full.Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)))
                throw new CliUsageException("Output must not replace workspace or edit state files.");
            if (Directory.Exists(path)) throw new IOException("Output path is a directory.");
            if (File.Exists(path) && !args.Flag("overwrite")) throw new IOException("Output exists; explicit --overwrite required.");
        }
        private async Task Write(string path, byte[] bytes, bool projectSave = false)
        {
            token.ThrowIfCancellationRequested(); EnsureWritable(path, projectSave);
            var fullPath = Path.GetFullPath(path); var directory = Path.GetDirectoryName(fullPath)!; Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try { await File.WriteAllBytesAsync(temporary, bytes, token); token.ThrowIfCancellationRequested(); File.Move(temporary, fullPath, args.Flag("overwrite")); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private async Task Save(string path, StudioProject project)
        {
            if (File.Exists(ProjectEditSession.StatePath(path))) throw new CliConflictException("Destination has persistent edits; use project save or a different output path.");
            EnsureWritable(path, true); token.ThrowIfCancellationRequested();
            if (args.Get("project") is { } source && Path.GetFullPath(source).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
                && (await ProjectFileService.Read(source, token)).Edited && args.Command is not "project migrate" && !args.Flag("replace-edited"))
                throw new CliUsageException("Edited project protected. Save to a new path or use project regenerate --replace-edited.");
            // A staged save retains the shared serializer and strict validation.
            // In-place migration also keeps the desktop's pre-migration backup.
            if (args.Command == "project migrate" && File.Exists(path)) await WorkspaceService.SaveProject(path, project);
            else
            {
                ProjectFileService.Validate(project);
                var upgraded = project with { Version = WorkspaceService.CurrentProjectVersion, Document = UnicodeGrid.Upgrade(project.Document), GeneratedDocument = project.GeneratedDocument is null ? null : UnicodeGrid.Upgrade(project.GeneratedDocument) };
                var bytes = JsonSerializer.SerializeToUtf8Bytes(upgraded);
                if (bytes.Length > ProjectFileService.MaximumBytes) throw new ArgumentException("Project exceeds 100MB.");
                await Write(path, bytes, true);
            }
        }
        private async Task EmitProject(StudioProject project)
        {
            if (args.Get("output") is { } outputPath)
            {
                EnsureWritable(outputPath);
                if (args.Get("save-project") is { } projectPath && Path.GetFullPath(outputPath).Equals(Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase))
                    throw new CliUsageException("--output and --save-project must use different paths.");
            }
            if (args.Get("format", "TXT") is "PNG" or "JPEG" or "GIF" && !args.Has("output")) throw new CliUsageException("Bitmap export requires --output.");
            if (args.Get("save-project") is { } destination) await Save(destination, project);
            await Emit(project.Document);
        }
        private async Task EmitText(string text)
        {
            if (args.Get("output") is { } destination) EnsureWritable(destination);
            await ApplyEdit(text);
            if (args.Get("output") is { } path) { await Write(path, Utf8.GetBytes(text)); if (Json) await Report(new { path = Path.GetFullPath(path) }); }
            else if (Json) await Report(new { text }); else await output.WriteAsync(text);
        }
        private async Task Emit(AsciiDocument document)
        {
            document.Validate(); var format = args.Get("format", "TXT")!;
            if (format is "PNG" or "JPEG" or "GIF" && !args.Has("output")) throw new CliUsageException("Bitmap export requires --output.");
            var bytes = await Task.Run(() => ExportBytes(document, format), token);
            token.ThrowIfCancellationRequested();
            if (args.Get("output") is { } destination) EnsureWritable(destination);
            await ApplyEdit(document.Text);
            if (args.Get("output") is { } path) { await Write(path, bytes); if (Json) await Report(new { path = Path.GetFullPath(path), format, document.Width, document.Height }); }
            else
            {
                if (format is "PNG" or "JPEG" or "GIF") throw new CliUsageException("Bitmap export requires --output.");
                if (Json) await Report(new { format, document, text = Utf8.GetString(bytes) }); else await output.WriteAsync(Utf8.GetString(bytes));
            }
        }
        private async Task ApplyEdit(string text)
        {
            if (!args.Flag("apply")) return;
            using var session = await ProjectEditSession.Open(args.Require("project"), token);
            if (session.Current.Document.Text != applySourceText) throw new CliConflictException("Edited result changed while preparing the tool output; retry.");
            await session.Edit(text, token);
        }
        private byte[] ExportBytes(AsciiDocument document, string format) => format switch
        {
            "TXT" => Utf8.GetBytes(document.Text), "HTML" => Utf8.GetBytes(ExportService.Html(document)), "SVG" => Utf8.GetBytes(ExportService.Svg(document)),
            "ANSI" => Utf8.GetBytes(ExportService.Ansi(document)), "JSON" => Utf8.GetBytes(ExportService.Json(document)), "Markdown" => Utf8.GetBytes(ExportService.Markdown(document)),
            "PNG" or "JPEG" or "GIF" => ImagingService.Render(document, (float)args.Number("font-size", 14), transparent: args.Flag("transparent"),
                format: format == "JPEG" ? ImageFormat.Jpeg : format == "GIF" ? ImageFormat.Gif : ImageFormat.Png, scale: args.Integer("scale", 1)),
            _ => throw new CliUsageException("Unsupported --format. Use TXT/PNG/JPEG/GIF/HTML/SVG/ANSI/JSON/Markdown.")
        };
        private async Task<int> Batch()
        {
            if (args.Flag("native-size") && args.Has("rows")) throw new CliUsageException("Native size derives rows automatically.");
            var directory = Path.GetFullPath(args.Require("input")); var destination = Path.GetFullPath(args.Require("output"));
            var format = args.Get("format", "TXT")!; var extension = format == "Markdown" ? ".md" : "." + format.ToLowerInvariant();
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Input directory does not exist.");
            if (destination.Equals(directory, StringComparison.OrdinalIgnoreCase) || destination.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Batch output must be outside the input directory.");
            Directory.CreateDirectory(destination);
            var results = new List<object>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var failures = 0;
            var files = Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = args.Flag("recursive"), IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint })
                .Where(path => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tiff", ".tif" }.Contains(Path.GetExtension(path).ToLowerInvariant())).Take(1001).ToArray();
            if (files.Length > 1000) throw new CliUsageException("Batch limited to 1000 inputs.");
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested(); var name = Path.GetFileNameWithoutExtension(file); var index = 1; var candidate = name + extension;
                while (!names.Add(candidate)) candidate = name + "-" + (++index).ToString(CultureInfo.InvariantCulture) + extension;
                var target = Path.Combine(destination, candidate);
                try
                {
                    EnsureWritable(target); var project = await Image(file); var bytes = ExportBytes(project.Document, format); await Write(target, bytes);
                    results.Add(new { input = file, output = target, ok = true });
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException)
                { failures++; results.Add(new { input = file, ok = false, code = exception is IOException or UnauthorizedAccessException ? "io" : "conversion", message = exception.Message }); }
            }
            await Report(new { total = files.Length, failures, files = results }); return failures == 0 ? 0 : 5;
        }
    }
}
