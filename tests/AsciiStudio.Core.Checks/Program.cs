using AsciiStudio.Core;

var passed=0;
void Check(string name,Action action){action();passed++;Console.WriteLine($"PASS {name}");}
void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
byte[] pixels=[0,0,0,255,255,255,255,255,255,0,0,255,0,0,255,0];
Check("fixed grid dimensions",()=>{
    var d=ImageConverter.Convert(pixels,2,2,new(){Columns=240,Rows=135});d.Validate();
    Assert(d.Width==240&&d.Height==135,"Wrong fixed grid dimensions");
});
Check("automatic aspect correction",()=>{
    var d=ImageConverter.Convert(pixels,2,2,new(){Columns=240,CellAspect=.5});Assert(d.Height==120,"Wrong automatic rows");
});
Check("full HD character grid",()=>{
    var d=ImageConverter.Convert(pixels,2,2,new(){Columns=1920,Rows=1080});d.Validate();Assert(d.Width*d.Height==2_073_600,"Full HD was truncated");
});
Check("grid boundaries rejected",()=>{
    try{ImageConverter.Convert(pixels,2,2,new(){Columns=2001});throw new Exception("Oversized columns accepted");}catch(ArgumentOutOfRangeException){}
    try{ImageConverter.Convert(pixels,2,2,new(){Columns=120,Rows=2001});throw new Exception("Oversized rows accepted");}catch(ArgumentOutOfRangeException){}
});
Check("tall auto grid is not silently distorted",()=>{
    try{ImageConverter.Convert(new byte[4*100],1,100,new(){Columns=240});throw new Exception("Tall image was silently clamped");}catch(ArgumentException){}
});
Check("legacy default options retain aspect",()=>{
    var d=ImageConverter.Convert(pixels,2,2,new());Assert(d.Width==120&&d.Height==60,"Legacy defaults changed");
});
Check("cancellation interrupts conversion",()=>{
    using var cts=new CancellationTokenSource();cts.Cancel();
    try{ImageConverter.Convert(pixels,2,2,new(){Columns=1920,Rows=1080},cts.Token);throw new Exception("Canceled conversion succeeded");}catch(OperationCanceledException){}
});
Check("alpha composite and density extremes",()=>{
    var d=ImageConverter.Convert([0,0,0,0],1,1,new(){Columns=8,Rows=1,Characters=" @"});Assert(d.Text==new string(' ',8),"Transparent pixels became dark");
    var black=ImageConverter.Convert([0,0,0,255],1,1,new(){Columns=8,Rows=1,Characters=" @"});Assert(black.Text==new string('@',8),"Black pixels lost density");
});
byte[] transformPixels = Enumerable.Range(0, 6).SelectMany(i => new byte[] { (byte)i, (byte)(20 + i), (byte)(40 + i), (byte)(200 + i) }).ToArray();
int[][] rotations = [[0, 1, 2, 3, 4, 5], [4, 2, 0, 5, 3, 1], [5, 4, 3, 2, 1, 0], [1, 3, 5, 0, 2, 4]];
for (var turn = 0; turn < 4; turn++)
for (var flipX = 0; flipX < 2; flipX++)
for (var flipY = 0; flipY < 2; flipY++)
{
    var angle = turn; var horizontal = flipX != 0; var vertical = flipY != 0;
    Check($"rotation {angle * 90} and flips {horizontal}/{vertical}", () =>
    {
        var output = ImageTransforms.Apply(transformPixels, 2, 3, new(QuarterTurns: angle, FlipHorizontal: horizontal, FlipVertical: vertical));
        var expectedWidth = angle % 2 == 0 ? 2 : 3; var expectedHeight = angle % 2 == 0 ? 3 : 2;
        Assert(output.Width == expectedWidth && output.Height == expectedHeight, "Rotation dimensions incorrect");
        for (var y = 0; y < expectedHeight; y++) for (var x = 0; x < expectedWidth; x++)
        {
            var row = vertical ? expectedHeight - 1 - y : y;
            var column = horizontal ? expectedWidth - 1 - x : x;
            var label = rotations[angle][row * expectedWidth + column];
            Assert(output.Pixels.AsSpan((y * expectedWidth + x) * 4, 4).SequenceEqual(transformPixels.AsSpan(label * 4, 4)), "Pixel color/alpha or orientation changed");
        }
    });
}
Check("crop before rotation", () =>
{
    var output = ImageTransforms.Apply(transformPixels, 2, 3, new(Left: 50, Width: 50, QuarterTurns: 1));
    Assert(output.Width == 3 && output.Height == 1, "Crop dimensions incorrect");
    Assert(new[] { output.Pixels[0], output.Pixels[4], output.Pixels[8] }.SequenceEqual(new byte[] { 5, 3, 1 }), "Wrong crop rotation order");
});
Check("fractional tiny crop remains at least one pixel", () =>
{
    var output = ImageTransforms.Apply(transformPixels, 2, 3, new(Left: 99.9, Top: 99.9, Width: .1, Height: .1));
    Assert(output.Width == 1 && output.Height == 1 && output.Pixels[0] == 5, "Tiny crop lost its last pixel");
});
Check("invalid geometry rejected", () =>
{
    foreach (var geometry in new ImageGeometry[] { new(Left: -1), new(Width: 0), new(Left: 20), new(Height: double.NaN), new(QuarterTurns: 4) })
    {
        try { ImageTransforms.Apply(transformPixels, 2, 3, geometry); throw new Exception("Invalid crop accepted"); } catch (ArgumentException) { }
    }
    try { ImageTransforms.Apply([0], 2, 3, new()); throw new Exception("Invalid pixels accepted"); } catch (ArgumentException) { }
});
Check("transform cancellation and original pixels", () =>
{
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { ImageTransforms.Apply(transformPixels, 2, 3, new(), cancellation.Token); throw new Exception("Canceled transform succeeded"); } catch (OperationCanceledException) { }
    var output = ImageTransforms.Apply(transformPixels, 2, 3, new()); output.Pixels[0] = 255;
    Assert(transformPixels[0] == 0, "Transform overwrote the source");
});
Check("geometry undo redo and divergent edits", () =>
{
    var history = new ImageGeometryHistory(); history.Apply(new(QuarterTurns: 1)); history.Apply(new(FlipHorizontal: true));
    Assert(history.Undo().QuarterTurns == 1 && history.CanRedo, "Undo failed");
    Assert(history.Redo().FlipHorizontal, "Redo failed"); history.Undo(); history.Apply(new(QuarterTurns: 2));
    Assert(!history.CanRedo && history.Current.QuarterTurns == 2, "New edit retained invalid redo");
    history.Apply(new()); Assert(history.Undo().QuarterTurns == 2, "Reset cannot be undone");
    history.Clear(); Assert(!history.CanUndo && !history.CanRedo && history.Current == new ImageGeometry(), "Import did not reset history");
});
Check("geometry history is bounded and unchanged edits are ignored", () =>
{
    var history = new ImageGeometryHistory(); history.Apply(new()); Assert(!history.CanUndo, "Unchanged state added to history");
    for (var i = 1; i <= 60; i++) history.Apply(new(QuarterTurns: i % 4));
    var count = 0; while (history.CanUndo) { history.Undo(); count++; }
    Assert(count == 40, "History exceeded its memory bound");
});
Check("geometry JSON round trip", () =>
{
    var geometry = new ImageGeometry(10, 20, 50, 60, 3, true, false);
    var loaded = System.Text.Json.JsonSerializer.Deserialize<ImageGeometry>(System.Text.Json.JsonSerializer.Serialize(geometry));
    Assert(loaded == geometry, "Saved geometry changed");
    Assert(System.Text.Json.JsonSerializer.Deserialize<ImageGeometry>("{}") == new ImageGeometry(), "Missing fields broke defaults");
});
Check("font metrics survive project JSON", () =>
{
    var document = AsciiDocument.FromText("abc") with { FontFamily = "Courier New", CellWidth = 7.8, CellHeight = 15.1 };
    var restored = System.Text.Json.JsonSerializer.Deserialize<AsciiDocument>(System.Text.Json.JsonSerializer.Serialize(document))!;
    restored.Validate(); Assert(restored == document, "Font metadata changed");
});
Check("SVG font metrics use invariant numbers and escaped family", () =>
{
    var previous = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = new("fr-FR");
        var svg = ExportService.Svg(AsciiDocument.FromText("a") with { FontFamily = "A<&", CellWidth = 7.5, CellHeight = 15.5 });
        Assert(svg.Contains("width=\"47.5\"") && svg.Contains("A&lt;&amp;"), "SVG metrics or font escaping failed");
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
});
Check("authenticated ciphers round trip and reject altered metadata", () =>
{
    const string text = "ASCII\n测试 🙂";
    foreach (var name in CryptoTools.Modern.Where(n => !n.StartsWith("RSA-") && CryptoTools.IsSupported(n)))
    {
        var cipher = CryptoTools.Apply(name, text, "secret");
        Assert(CryptoTools.Apply(name, cipher, "secret", true) == text, "Cipher round trip failed: " + name);
        var changed = cipher.Replace("600000", "99999999");
        try { CryptoTools.Apply(name, changed, "secret", true); throw new Exception("Unbounded KDF metadata accepted"); } catch (ArgumentException) { }
    }
    var protectedText = CryptoTools.Apply("AES-256-GCM", text, "secret");
    try { CryptoTools.Apply("AES-256-GCM", protectedText, "wrong", true); throw new Exception("Wrong password accepted"); } catch (System.Security.Cryptography.CryptographicException) { }
});
Check("RSA hybrid cipher supports arbitrary Unicode text", () =>
{
    var keys = CryptoTools.GenerateRsaKeys(); const string name = "RSA-OAEP-SHA256 + AES-256-GCM";
    var cipher = CryptoTools.Apply(name, "测试\nASCII", keyPem: keys.PublicKey);
    Assert(CryptoTools.Apply(name, cipher, decrypt: true, keyPem: keys.PrivateKey) == "测试\nASCII", "RSA hybrid round trip failed");
});
Check("encoding and traditional methods round trip", () =>
{
    foreach (var name in CryptoTools.Encodings)
    {
        var encoded = CryptoTools.Apply(name, "abc测试🙂\n");
        Assert(CryptoTools.Apply(name, encoded, decrypt: true) == "abc测试🙂\n", "Encoding round trip failed: " + name);
    }
    foreach (var name in CryptoTools.Traditional)
    {
        var key = name == "Caesar" ? "-27" : name == "Rail Fence" ? "3" : "secret";
        Assert(CryptoTools.Apply(name, CryptoTools.Apply(name, "Abc 测试🙂!\n", key), key, true) == "Abc 测试🙂!\n", "Traditional round trip failed: " + name);
    }
    Assert(CryptoTools.Apply("Base32", "foo") == "MZXW6===", "RFC 4648 Base32 vector failed");
});
Check("digest known value and input boundaries", () =>
{
    Assert(CryptoTools.Apply("SHA-256", "abc") == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA256 vector failed");
    try { CryptoTools.Apply("SHA-256", "abc", decrypt: true); throw new Exception("Digest was decrypted"); } catch (ArgumentException) { }
    try { CryptoTools.Apply("AES-256-GCM", new string('a', CryptoTools.InputLimit + 1), "secret"); throw new Exception("Oversize input accepted"); } catch (ArgumentException) { }
});
Check("line comments preserve art whitespace", () =>
{
    Assert(CommentTools.Wrap("  abc\r\nxyz \n", "Python") == "#   abc\n# xyz \n# ", "Line wrapping changed whitespace");
    foreach (var language in CommentTools.Languages) Assert(CommentTools.Wrap("abc", language.Name).Contains("abc"), "Language missing: " + language.Name);
});
Check("block comments reject ending conflicts", () =>
{
    Assert(CommentTools.Wrap("abc", "CSS") == "/*\nabc\n*/", "CSS wrapper failed");
    foreach (var language in new[] { "CSS", "C++", "SQL" })
    { try { CommentTools.Wrap("*/", language, true); throw new Exception("Unsafe block accepted"); } catch (ArgumentException) { } }
    try { CommentTools.Wrap("a--b", "XML"); throw new Exception("Invalid XML accepted"); } catch (ArgumentException) { }
    try { CommentTools.Wrap("abc", "Python", true); throw new Exception("Python string treated as comment"); } catch (ArgumentException) { }
});
Check("ANSI color palette true color and iCE", () =>
{
    var parsed = AnsiArt.Parse("\x1b[1;31;44mA\x1b[38;5;214;48;2;1;2;3mB\x1b[0;5;41mC", 20, true).Document;
    Assert(parsed.Colors![0] == 0xFFFF5555 && parsed.BackgroundColors![0] == 0xFF0000AA, "16 color palette incorrect");
    Assert(parsed.Colors![1] == 0xFFFFAF00 && parsed.BackgroundColors![1] == 0xFF010203, "256 or true color incorrect");
    Assert(parsed.BackgroundColors![2] == 0xFFFF5555, "iCE bright background incorrect");
    var inverse = AnsiArt.Parse("\x1b[31;44;7mX\x1b[27;8mY", 20).Document;
    Assert(inverse.Colors![0] == 0xFF0000AA && inverse.BackgroundColors![0] == 0xFFAA0000 && inverse.Colors[1] == inverse.BackgroundColors[1], "Reverse or conceal failed");
});
Check("ANSI cursor erase delayed wrap and saved cursor", () =>
{
    var screen = AnsiArt.Parse("abcdef\rXY\x1b[3G\x1b[KZ\x1b[s\x1b[2;4HQ\x1b[uR", 20).Document;
    Assert(screen.Text.Split('\n')[0].StartsWith("XYZR") && screen.Text.Split('\n')[1][3] == 'Q', "Cursor state failed");
    Assert(AnsiArt.Parse(new string('a', 20) + "\r\nb", 20).Document.Height == 2, "Full line introduced blank row");
    Assert(AnsiArt.Parse(new string('a', 20) + "b", 20).Document.Height == 2, "Long line did not wrap");
    var erased = AnsiArt.Parse("abc\x1b[2J", 20).Document;
    Assert(erased.Text.Trim().Length == 0, "Erase display failed");
});
Check("ANSI safe unknown sequences truncated input and resource limits", () =>
{
    var parsed = AnsiArt.Parse("A\x1b]8;;https://example.com\aB\x1b[?25lC\x1b[", 20);
    Assert(parsed.Document.Text.Trim() == "ABC" && parsed.IgnoredSequences == 2 && parsed.IncompleteSequences == 1, "Unknown control string leaked");
    try { AnsiArt.Parse("\x1b[2001;1Hx", 20); throw new Exception("Unbounded cursor accepted"); } catch (ArgumentException) { }
    try { AnsiArt.Decode(new byte[AnsiArt.InputLimit + 1]); throw new Exception("Oversize file accepted"); } catch (ArgumentException) { }
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { AnsiArt.Parse("abc", cancellation: cancellation.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
});
Check("ANSI CP437 UTF8 SAUCE and malformed comments", () =>
{
    Assert(AnsiArt.Decode([0xDA, 0xC4, 0xBF]).Text == "┌─┐", "CP437 box drawing failed");
    Assert(AnsiArt.Decode(System.Text.Encoding.UTF8.GetBytes("测试")).Text == "测试", "UTF8 failed");
    var body = System.Text.Encoding.ASCII.GetBytes("abc\x1a"); var bytes = new byte[body.Length + 128]; body.CopyTo(bytes, 0);
    System.Text.Encoding.ASCII.GetBytes("SAUCE00").CopyTo(bytes, body.Length);
    System.Text.Encoding.ASCII.GetBytes("Title").CopyTo(bytes, body.Length + 7);
    bytes[body.Length + 94] = 1; bytes[body.Length + 96] = 80; bytes[body.Length + 104] = 255;
    var source = AnsiArt.Decode(bytes);
    Assert(source.Text == "abc" && source.Metadata?.Title == "Title" && source.Metadata.Width == 80 && source.MetadataWarnings == 1, "SAUCE metadata separation failed");
    Assert(AnsiArt.Decode(System.Text.Encoding.ASCII.GetBytes("abcSAUCE00")).Text == "abcSAUCE00", "Short record crashed");
});
Check("ANSI background export and document JSON compatibility", () =>
{
    var original = AnsiArt.Parse("\x1b[31;44mABC", 20).Document;
    var restored = AnsiArt.Parse(ExportService.Ansi(original), 20).Document;
    Assert(original.Text == restored.Text && original.Colors!.SequenceEqual(restored.Colors!) && original.BackgroundColors!.SequenceEqual(restored.BackgroundColors!), "ANSI round trip lost colors");
    Assert(ExportService.Html(original).Contains("background-color:#0000AA") && ExportService.Svg(original).Contains("fill=\"#0000AA\""), "Background export missing");
    var json = System.Text.Json.JsonSerializer.Deserialize<AsciiDocument>(ExportService.Json(original))!;
    Assert(json.BackgroundColors!.SequenceEqual(original.BackgroundColors!), "JSON lost backgrounds");
    Assert(AsciiDocument.FromText("abc").BackgroundColors is null, "Legacy document background changed");
    try { (original with { BackgroundColors = [0] }).Validate(); throw new Exception("Invalid background grid accepted"); } catch (ArgumentException) { }
});
Check("generator recipes restore all seven sources", () =>
{
    for (var kind = 0; kind < 7; kind++)
    {
        var recipe = new GeneratorRecipe(kind, 2, "first\nsecond", 32, 12, 1234);
        var restored = GeneratorRecipe.FromParameters(recipe.ToParameters(), recipe.Text);
        Assert(restored == recipe && restored.Generate() == recipe.Generate(), "Generator source round trip changed output");
        AsciiDocument.FromText(restored.Generate()).Validate();
    }
});
Check("generator invalid project parameters rejected", () =>
{
    void Rejected(Action action) { try { action(); throw new Exception("Invalid generator parameters accepted"); } catch (ArgumentException) { } }
    Rejected(() => GeneratorRecipe.FromParameters(null, ""));
    var parameters = new GeneratorRecipe().ToParameters(); parameters["schema"] = "2";
    Rejected(() => GeneratorRecipe.FromParameters(parameters, ""));
    parameters["schema"] = "1"; parameters["seed"] = "1.5";
    Rejected(() => GeneratorRecipe.FromParameters(parameters, ""));
    Rejected(() => new GeneratorRecipe(Kind: 7).Generate());
    Rejected(() => new GeneratorRecipe(Kind: 2, Width: 101).Generate());
    Rejected(() => new GeneratorRecipe(Text: new string('x', 2001)).Generate());
    Rejected(() => new GeneratorRecipe(Seed: -1).Generate());
    var normal = new GeneratorRecipe().ToParameters();
    Assert(GeneratorRecipe.FromParameters(normal, "a\r\nb").Text == "a\nb", "Project line endings were not normalized");
});
Check("bounded creation history merge undo redo and divergence", () =>
{
    var history = new BoundedHistory<string>(text => text.Length * 2, 3, 1024);
    history.Push("initial"); history.Push("a"); history.Push("ab", merge: true);
    Assert(history.Count == 2 && history.Undo() == "initial", "Typing transaction did not merge");
    Assert(history.Redo() == "ab", "Redo failed"); history.Undo(); history.Push("different");
    Assert(!history.CanRedo && history.PeekUndo() == "initial", "Divergent change retained redo");
    history.Push("three"); history.Push("four"); history.Push("five");
    Assert(history.Count == 4 && history.Undo() == "four", "Step limit changed current state");
});
Check("bounded creation history budgets retain usable current state", () =>
{
    var history = new BoundedHistory<string>(text => text.Length * 2, 100, 12);
    history.Push("one"); history.Push("two"); history.Push("six");
    Assert(history.Count == 2 && history.RetainedBytes == 12, "Budget was not enforced");
    history.Push(new string('x', 20));
    Assert(history.Count == 1 && !history.CanUndo && history.Current.Length == 20, "Oversized current state lost");
    try { history.Undo(); throw new Exception("Empty undo accepted"); } catch (InvalidOperationException) { }
});
Check("Unicode clusters and stable terminal widths", () =>
{
    foreach (var (text, expected) in new (string, int)[] { ("ABC", 3), ("测试A", 5), ("e\u0301", 1), ("👨‍👩‍👧‍👦", 2), ("🇨🇳", 2), ("1️⃣", 2), ("中👩🏽‍💻x", 5), ("╔═╗", 3), ("\u0301", 0), ("\U00020000", 2) })
        Assert(UnicodeGrid.Width(text) == expected, $"Wrong width for {text}");
    Assert(UnicodeGrid.Glyphs("👨‍👩‍👧‍👦").Count() == 1, "Emoji sequence was split");
    Assert(AsciiDocument.FromText("中\tx\ne\u0301\tZ").Text == "中  x\ne\u0301   Z", "Tab stops ignored display width");
});
Check("Unicode validation and border alignment", () =>
{
    var bordered = Generators.Border("测试\ne\u0301\n👨‍👩‍👧‍👦", 2);
    Assert(bordered.Split('\n').Select(UnicodeGrid.Width).Distinct().Count() == 1, "Unicode border was misaligned");
    Assert(AsciiDocument.FromText("中e\u0301😀").Width == 5, "Grid uses UTF-16 length");
    try { AsciiDocument.FromText("\ud800"); throw new Exception("Unpaired surrogate accepted"); } catch (ArgumentException) { }
    try { (AsciiDocument.FromText("测试") with { Width = 2 }).Validate(); throw new Exception("Wide text overflow accepted"); } catch (ArgumentException) { }
    try { new AsciiDocument { Width = 10, Height = 0, BackgroundColors = [] }.Validate(); throw new Exception("Zero row color grid accepted"); } catch (ArgumentException) { }
    try { ImageConverter.Convert(pixels, 2, 2, new() { Characters = " 中" }); throw new Exception("Wide image ramp accepted"); } catch (ArgumentException) { }
});
Check("Unicode legacy foreground and background migration", () =>
{
    var legacy = new AsciiDocument { Text = "中e\u0301😀!", Width = 6, Height = 1, Colors = [1,2,3,4,5,6], BackgroundColors = [11,12,13,14,15,16] };
    var upgraded = UnicodeGrid.Upgrade(legacy);
    Assert(upgraded.GridVersion == 1 && upgraded.Width == 6, "Wrong migrated grid");
    Assert(upgraded.Colors!.SequenceEqual(new uint[] {1,1,2,4,4,6}) && upgraded.BackgroundColors!.SequenceEqual(new uint[] {11,11,12,14,14,16}), "Migration split glyph colors");
    Assert(ReferenceEquals(UnicodeGrid.Upgrade(upgraded), upgraded), "Migration is not idempotent");
    var grown = UnicodeGrid.Upgrade(new AsciiDocument { Text = "测试", Width = 2, Height = 1, Colors = [1,2] });
    Assert(grown.Width == 4 && grown.Colors!.SequenceEqual(new uint[] {1,1,2,2}), "CJK migration did not expand columns");
});
Check("Unicode SVG HTML and ANSI keep clusters and colors", () =>
{
    var document = AsciiDocument.FromText("中e\u0301😀!") with { Colors = [0xFFFF0000,0xFFFF0000,0xFF00FF00,0xFF0000FF,0xFF0000FF,0xFFFFFFFF] };
    var svg = ExportService.Svg(document);
    var xml = System.Xml.Linq.XDocument.Parse(svg);
    var nodes = xml.Descendants().Where(n => n.Name.LocalName == "text").ToArray();
    Assert(nodes.Length == 4 && nodes[1].Value == "e\u0301" && nodes[2].Value == "😀" && (string?)nodes[3].Attribute("x") == "65", "SVG split glyphs or used UTF-16 positions");
    Assert(ExportService.Html(document).Contains("width:18px") && ExportService.Html(document).Contains("e\u0301"), "HTML missing fixed widths");
    var restored = AnsiArt.Parse(ExportService.Ansi(document), 20).Document;
    Assert(restored.Text.TrimEnd() == document.Text && restored.Colors!.Take(6).SequenceEqual(document.Colors!), "ANSI Unicode round trip changed glyphs or colors");
});
Check("Unicode ANSI wide cursor wrap and overwrite", () =>
{
    var parsed = AnsiArt.Parse(new string('a',19) + "中Z", 20).Document;
    Assert(parsed.Height == 2 && parsed.Text.Split('\n')[1].StartsWith("中Z"), "Wide glyph did not wrap before last column");
    var overwritten = AnsiArt.Parse("中\x1b[2GX",20).Document;
    Assert(overwritten.Text.StartsWith(" X"), "Overwriting continuation left broken wide glyph");
    var combined = AnsiArt.Parse("e\x1b[31m\u0301X",20).Document;
    Assert(combined.Text.StartsWith("e\u0301X"), "Color escape broke combining mark");
});
Check("viewport index bounded pages retain complete text", () =>
{
    var text = string.Join('\n', Enumerable.Range(0, 1500).Select(i => $"{i:D4}:" + new string('x', 195)));
    var index = new DocumentViewIndex(AsciiDocument.FromText(text));
    Assert(index.IsPaged && index.Pages.Length > 1 && index.RowStarts.Length == 1500, "Large text not indexed");
    Assert(string.Concat(index.Pages.Select(p => text.Substring(p.Start, p.Length))) == text, "Page segmentation lost text");
    Assert(index.Pages.All(p => p.Length <= 64012) && index.Line(999).StartsWith("0999:"), "Page budget or row index invalid");
    var page = index.Pages[1]; var replacement = "edit\r\n中";
    Assert(index.ReplacePage(1, replacement) == text[..page.Start] + "edit\n中" + text[(page.Start + page.Length)..], "Paged edit replaced more than one page");
    Assert(index.PageAt(page.Start) == 1 && index.PageAt(text.Length) == index.Pages.Length - 1, "Boundary page lookup failed");
});
Check("viewport pages preserve graphemes and positions", () =>
{
    var cluster = "👨‍👩‍👧‍👦";
    var text = new string('a', 63999) + cluster + new string('b', 150000);
    var index = new DocumentViewIndex(AsciiDocument.FromText(text));
    Assert(index.Pages[0].Length == 63999 + cluster.Length && index.Pages[1].Start == index.Pages[0].Length, "Page split an emoji cluster");
    Assert(DocumentViewIndex.Prefix(text, 64000).Length == 63999 && DocumentViewIndex.Prefix(cluster, 1) == "", "Prefix split a cluster");
    var small = new DocumentViewIndex(AsciiDocument.FromText("中é😀\nxyz"));
    Assert(!small.IsPaged && small.Position(2) == (0, 3) && small.Position(7) == (1, 1), "Unicode selection coordinates wrong");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { new DocumentViewIndex(small.Document, cancelled.Token); throw new Exception("Cancelled index accepted"); } catch (OperationCanceledException) { }
});
Check("viewport physical allocation budget at any DPI", () =>
{
    foreach (var density in new double[] { 1, 1.25, 1.5, 2, 3, 8 })
    {
        var region = ViewportRegion.Create(12000, 34000, 7680, 4320, density);
        Assert(region.PixelWidth <= 4096 && region.PixelHeight <= 4096 && (long)region.PixelWidth * region.PixelHeight <= 4_000_000, "Viewport exceeded raster budget");
        Assert(region.X == 11984 && region.Y == 33984, "Viewport forgot scroll origin");
    }
    Assert(ViewportRegion.Create(0, 0, 480, 480, 1.25).PixelWidth == 640, "DPI density lost at normal sizes");
    try { ViewportRegion.Create(0, 0, double.NaN, 1, 1); throw new Exception("Invalid region accepted"); } catch (ArgumentException) { }
});
Check("measured density follows coverage rather than order", () =>
{
    var d = ImageConverter.Convert([0, 0, 0, 255], 1, 1, new() { Columns = 8, Rows = 1, Characters = "@ .", MeasureGlyphDensity = true }, glyphs: [new('@', .8), new(' ', 0), new('.', .1)]);
    Assert(d.Text == new string('@', 8), "Measured density ignored the actual font coverage");
    try { ImageConverter.Convert(pixels, 2, 2, new() { MeasureGlyphDensity = true }); throw new Exception("Missing coverage accepted"); } catch (ArgumentException) { }
});
Check("Braille dot orientation and monochrome half blocks", () =>
{
    var input = Enumerable.Repeat((byte)255, 16 * 4 * 4).ToArray(); input[0] = input[1] = input[2] = 0;
    var d = ImageConverter.Convert(input, 16, 4, new() { Columns = 8, Rows = 1, Style = ImageArtStyle.Braille });
    d.Validate(); Assert(d.Text == "⠁       ", "Braille first dot or empty glyph incorrect");
    for (var dot = 0; dot < 8; dot++)
    {
        var isolated = Enumerable.Repeat((byte)255, 16 * 4 * 4).ToArray(); var offset = ((dot / 2) * 16 + dot % 2) * 4;
        isolated[offset] = isolated[offset + 1] = isolated[offset + 2] = 0;
        int[] positions = [0,3,1,4,2,5,6,7];
        Assert(ImageConverter.Convert(isolated, 16, 4, new() { Columns = 8, Rows = 1, Style = ImageArtStyle.Braille }).Text[0] == (char)(0x2800 + (1 << positions[dot])), "Braille bit position incorrect");
    }
    var half = Enumerable.Repeat((byte)255, 8 * 2 * 4).ToArray(); half[0] = half[1] = half[2] = 0;
    Assert(ImageConverter.Convert(half, 8, 2, new() { Columns = 8, Rows = 1, Style = ImageArtStyle.HalfBlock }).Text[0] == '▀', "Top half mapping incorrect");
});
Check("half block retains separate colors and alpha", () =>
{
    var input = new byte[8 * 2 * 4]; for (var i = 0; i < 16; i++) { input[i * 4 + (i < 8 ? 0 : 2)] = 255; input[i * 4 + 3] = 255; }
    var d = ImageConverter.Convert(input, 8, 2, new() { Columns = 8, Rows = 1, Style = ImageArtStyle.HalfBlock, Color = true });
    d.Validate(); Assert(d.Text == new string('▀', 8) && d.Colors![0] == 0xFFFF0000 && d.BackgroundColors![0] == 0xFF0000FF, "Top/bottom colors lost");
    input[3] = 0; var alpha = ImageConverter.Convert(input, 8, 2, new() { Columns = 8, Rows = 1, Style = ImageArtStyle.HalfBlock, Color = true, PreserveTransparent = true });
    Assert(alpha.Text[0] == '▄' && alpha.BackgroundColors![0] == 0 && alpha.Colors![0] == 0xFF0000FF, "Transparent half became solid");
});
Check("transparent trim, blank and composition", () =>
{
    var input = new byte[3 * 3 * 4]; input[(1 * 3 + 1) * 4 + 3] = 128;
    var frame = ImageQualityConverter.Trim(input, 3, 3, 16); Assert(frame.Width == 1 && frame.Height == 1 && frame.Pixels[3] == 128, "Trim lost alpha or dimensions");
    var empty = ImageQualityConverter.Trim(new byte[36], 3, 3, 16); Assert(empty.Width == 1 && empty.Height == 1, "Transparent trim produced invalid size");
    var blank = ImageConverter.Convert(new byte[4], 1, 1, new() { Columns = 8, Rows = 1, PreserveTransparent = true, Color = true, Dither = DitherMode.Atkinson });
    Assert(blank.Text == new string(' ', 8) && blank.Colors!.All(c => c == 0), "Transparent cells received diffusion");
    var composed = ImageConverter.Convert(new byte[4], 1, 1, new() { Columns = 8, Rows = 1, Background = 0xFFFF0000, Color = true });
    Assert(composed.Colors![0] == 0xFFFF0000, "Chosen compositing background ignored");
});
Check("structure detects horizontal and vertical contours", () =>
{
    var input = new byte[8 * 8 * 4]; for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) { var p = (y * 8 + x) * 4; input[p] = input[p + 1] = input[p + 2] = x < 4 ? (byte)0 : (byte)255; input[p + 3] = 255; }
    var vertical = ImageConverter.Convert(input, 8, 8, new() { Columns = 8, Rows = 8, Style = ImageArtStyle.Structure });
    Assert(vertical.Text.Contains('|'), "Vertical contour missing");
    var rotated = ImageTransforms.Apply(input, 8, 8, new(QuarterTurns: 1));
    Assert(ImageConverter.Convert(rotated.Pixels, 8, 8, new() { Columns = 8, Rows = 8, Style = ImageArtStyle.Structure }).Text.Contains('-'), "Horizontal contour missing");
});
Check("adaptive filtering and mapping preserve cached inputs", () =>
{
    var sample = ImageQualityConverter.Sample(pixels, 2, 2, new() { Columns = 8, Rows = 8 }); var original = (float[])sample.Rgba.Clone();
    var filtered = ImageQualityConverter.Filter(sample, new() { AdaptiveStrength = 1 }); var density = (float[])filtered.Density.Clone(); var colors = (uint[])filtered.Colors.Clone();
    foreach (var mode in Enum.GetValues<DitherMode>()) ImageQualityConverter.Map(filtered, new() { Dither = mode, Color = true, PaletteMode = ImagePaletteMode.Ansi16 });
    Assert(original.SequenceEqual(sample.Rgba) && density.SequenceEqual(filtered.Density) && colors.SequenceEqual(filtered.Colors), "Cached stage mutated by later operation");
});
Check("palette modes, ANSI encoding and transparent export", () =>
{
    foreach (var mode in Enum.GetValues<ImagePaletteMode>())
    {
        var d = ImageConverter.Convert(pixels, 2, 2, new() { Columns = 8, Rows = 4, Color = true, PaletteMode = mode, PaletteSize = 4 }); d.Validate();
        if (mode == ImagePaletteMode.Ansi16) Assert(!ExportService.Ansi(d).Contains(";2;") && d.Colors!.All(c => ImagePalettes.Ansi16.Contains(c)), "ANSI16 exported as true color");
        if (mode == ImagePaletteMode.Ansi256) Assert(ExportService.Ansi(d).Contains(";5;"), "ANSI256 palette encoding missing");
        if (mode == ImagePaletteMode.Limited) Assert(d.Colors!.Distinct().Count() <= 4, "Color budget exceeded");
    }
    var alpha = AsciiDocument.FromText("▀") with { Colors = [0x80112233], BackgroundColors = [0] };
    Assert(ExportService.Html(alpha).Contains("#11223380") && ExportService.Svg(alpha).Contains("fill-opacity=\"0.502\"") && ExportService.Ansi(alpha).Contains("\x1b[49m"), "Export discarded transparency");
    try { ImagePalettes.Parse("#xx0000,#ffffff"); throw new Exception("Invalid palette accepted"); } catch (ArgumentException) { }
});
Check("quality bounds and cancellation", () =>
{
    foreach (var options in new ConversionOptions[] { new() { Style = (ImageArtStyle)99 }, new() { AdaptiveStrength = double.NaN }, new() { PaletteSize = 65 }, new() { AlphaThreshold = 0 }, new() { Style = ImageArtStyle.Braille, Columns = 2000, Rows = 2000 } })
    { try { ImageConverter.Convert(pixels, 2, 2, options); throw new Exception("Invalid or oversized quality options accepted"); } catch (ArgumentException) { } }
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    try { ImageConverter.Convert(pixels, 2, 2, new() { Style = ImageArtStyle.Braille }, cancel.Token); throw new Exception("Cancelled quality conversion accepted"); } catch (OperationCanceledException) { }
});
Check("cache owner/global budget and LRU release", () =>
{
    var cache = new BudgetCache(400, 650); cache.Put("a", 1, "one", 100); cache.Put("a", 2, "two", 100);
    Assert(cache.Get<string>("a", 1) == null && cache.OwnerBytes("a") <= 400, "Owner budget exceeded");
    cache.Put("b", 1, "three", 100); cache.Put("b", 2, "four", 100);
    cache.Put("c", 1, "five", 100);
    Assert(cache.RetainedBytes <= 650 && cache.Get<string>("a", 2) == null, "Global budget or LRU failed");
    cache.RemoveOwner("b"); cache.RemoveOwner("c"); Assert(cache.RetainedBytes == 0, "Owner release retained values");
    cache.Put("a", 3, "oversized", 1000); Assert(cache.RetainedBytes == 0, "Oversized entry retained");
});
Check("adaptive suppresses flat-region noise", () =>
{
    var rgba = new float[8 * 8 * 4];
    for (var i = 0; i < 64; i++) { rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 128; rgba[i * 4 + 3] = 1; }
    var center = 3 * 8 + 3; rgba[center * 4] = rgba[center * 4 + 1] = rgba[center * 4 + 2] = 140;
    var sample = new ImageSample(rgba, 8, 8, 8, 8, 1, 1);
    var original = ImageQualityConverter.Filter(sample, new()); var adaptive = ImageQualityConverter.Filter(sample, new() { AdaptiveStrength = 1 });
    Assert(Math.Abs(adaptive.Density[center] - original.Density[0]) < Math.Abs(original.Density[center] - original.Density[0]), "Flat noise was not reduced");
});
Check("block exports fill exact cell fractions", () =>
{
    var d = AsciiDocument.FromText("▀▄█") with { Colors = [0xFFFF0000, 0xFF00FF00, 0xFF0000FF] };
    var svg = ExportService.Svg(d); var html = ExportService.Html(d);
    Assert(svg.Contains("y=\"20\" width=\"9\" height=\"9\"") && svg.Contains("y=\"29\" width=\"9\" height=\"9\"") && svg.Contains("height=\"18\" fill=\"#0000FF\""), "Blocks depend on font baseline or gaps");
    Assert(html.Contains("linear-gradient") && html.Contains("▀") && html.Contains("▄"), "HTML did not retain block text and geometric appearance");
});
Check("cancellation stops an active quality computation", () =>
{
    var large = new byte[2048 * 2048 * 4];
    using var cancel = new CancellationTokenSource(); cancel.CancelAfter(10);
    try { ImageConverter.Convert(large, 2048, 2048, new() { Columns = 1000, Rows = 500, Style = ImageArtStyle.Braille }, cancel.Token); throw new Exception("Active computation ignored cancellation"); }
    catch (OperationCanceledException) { Assert(cancel.IsCancellationRequested, "Unexpected cancellation token"); }
});
Console.WriteLine($"{passed} checks passed.");
