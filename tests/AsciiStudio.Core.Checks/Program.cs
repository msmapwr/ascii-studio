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
Console.WriteLine($"{passed} checks passed.");
