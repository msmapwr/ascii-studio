using System.Buffers.Binary;
using System.Text;

namespace AsciiStudio.Core;

public sealed record SauceMetadata(string Title, string Author, string Group, string Date, int Width, int Height, bool IceColors, string[] Comments);
public sealed record AnsiSource(string Text, string Encoding, SauceMetadata? Metadata, int MetadataWarnings);
public sealed record AnsiResult(AsciiDocument Document, int IgnoredSequences, int IncompleteSequences, bool HasWideCharacters);

public static class AnsiArt
{
    public const int InputLimit = 4 * 1024 * 1024;
    public const int MaximumRows = 2000;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly uint[] Palette = [0xFF000000, 0xFFAA0000, 0xFF00AA00, 0xFFAA5500, 0xFF0000AA, 0xFFAA00AA, 0xFF00AAAA, 0xFFAAAAAA,
        0xFF555555, 0xFFFF5555, 0xFF55FF55, 0xFFFFFF55, 0xFF5555FF, 0xFFFF55FF, 0xFF55FFFF, 0xFFFFFFFF];

    public static AnsiSource Decode(byte[] bytes, string encoding = "Auto")
    {
        if (bytes.Length > InputLimit) throw new ArgumentException("ANSI 文件超过 4 MB 限制。");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp = Encoding.GetEncoding(437); var length = bytes.Length; var warnings = 0; SauceMetadata? metadata = null;
        if (length >= 128 && bytes.AsSpan(length - 128, 5).SequenceEqual("SAUCE"u8))
        {
            var record = bytes.AsSpan(length - 128, 128);
            if (record.Slice(5, 2).SequenceEqual("00"u8))
            {
                var end = length - 128; var comments = new List<string>();
                var count = record[104]; var commentStart = end - 5 - count * 64;
                if (count > 0)
                {
                    if (commentStart >= 0 && bytes.AsSpan(commentStart, 5).SequenceEqual("COMNT"u8))
                    {
                        for (var i = 0; i < count; i++) comments.Add(CleanField(cp.GetString(bytes, commentStart + 5 + i * 64, 64)));
                        end = commentStart;
                    }
                    else warnings++;
                }
                if (end > 0 && bytes[end - 1] == 26) end--;
                metadata = new(CleanField(cp.GetString(record.Slice(7, 35))), CleanField(cp.GetString(record.Slice(42, 20))),
                    CleanField(cp.GetString(record.Slice(62, 20))), CleanField(cp.GetString(record.Slice(82, 8))),
                    record[94] == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(96, 2)) : 0,
                    record[94] == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(98, 2)) : 0,
                    record[94] == 1 && (record[105] & 1) != 0, [.. comments]);
                length = end;
            }
            else { length -= 128; warnings++; }
        }
        var body = bytes.AsSpan(0, length); var selected = encoding;
        if (selected == "Auto")
        {
            try { Utf8.GetCharCount(body); selected = "UTF-8"; }
            catch (DecoderFallbackException) { selected = "CP437"; }
        }
        var text = selected switch
        {
            "UTF-8" => Utf8.GetString(body),
            "CP437" => cp.GetString(body),
            _ => throw new ArgumentException("不支持的 ANSI 编码。")
        };
        return new(text.TrimStart('\uFEFF'), selected, metadata, warnings);
    }

    private static string CleanField(string value) => new(value.Split('\0')[0].TrimEnd().Where(c => !char.IsControl(c)).ToArray());

    public static AnsiResult Parse(string text, int columns = 80, bool iceColors = false, string title = "ANSI art", CancellationToken cancellation = default)
    {
        if (columns is < 20 or > 300) throw new ArgumentException("ANSI 列数必须为 20–300。");
        if (text.Length > InputLimit) throw new ArgumentException("ANSI 文本超过 4 百万字符限制。");
        Utf8.GetByteCount(text); // Reject unpaired surrogates before document/export generation.
        var terminal = new Terminal(columns, iceColors);
        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested();
            var c = text[i];
            if (c == '\x1b')
            {
                if (++i >= text.Length) { terminal.Incomplete++; break; }
                c = text[i];
                if (c == '[')
                {
                    var start = i + 1;
                    while (++i < text.Length && text[i] is not (>= '@' and <= '~')) { if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested(); }
                    if (i >= text.Length) { terminal.Incomplete++; break; }
                    var arguments = text[start..i];
                    if (arguments.Length > 128 || arguments.Any(c => !char.IsAsciiDigit(c) && c != ';')) { terminal.Ignored++; continue; }
                    var parts = arguments.Split(';'); var values = new int[parts.Length]; var valid = true;
                    for (var p = 0; p < parts.Length; p++) { if (parts[p].Length > 0 && !int.TryParse(parts[p], out values[p])) valid = false; }
                    if (valid) terminal.Command(text[i], values); else terminal.Ignored++;
                }
                else if (c is ']' or 'P' or '^' or '_' or 'X')
                {
                    var osc = c == ']'; var ended = false;
                    while (++i < text.Length)
                    {
                        if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested();
                        if ((osc && text[i] == '\a') || (text[i] == '\x1b' && i + 1 < text.Length && text[i + 1] == '\\')) { if (text[i] == '\x1b') i++; ended = true; break; }
                    }
                    terminal.Ignored++; if (!ended) terminal.Incomplete++;
                }
                else if (c is '(' or ')' or '%' or '#' or ' ') { if (++i >= text.Length) terminal.Incomplete++; terminal.Ignored++; }
                else if (c == '7') terminal.Save();
                else if (c == '8') terminal.Restore();
                else if (c == 'D') terminal.Move(terminal.X, terminal.Y + 1);
                else if (c == 'E') terminal.Move(0, terminal.Y + 1);
                else if (c == 'M') terminal.Move(terminal.X, Math.Max(0, terminal.Y - 1));
                else if (c == 'c') terminal.Reset();
                else terminal.Ignored++;
                continue;
            }
            if (c == '\u001a') break;
            if (c is '\n' or '\v' or '\f') terminal.Move(0, terminal.Y + 1);
            else if (c == '\r') terminal.Move(0, terminal.Y);
            else if (c == '\b') terminal.Move(Math.Max(0, Math.Min(columns - 1, terminal.X) - 1), terminal.Y);
            else if (c == '\t') terminal.Move(Math.Min(columns - 1, (terminal.X / 8 + 1) * 8), terminal.Y);
            else if (char.IsControl(c)) { if (c != '\a') terminal.Ignored++; }
            else
            {
                var glyph = System.Globalization.StringInfo.GetNextTextElement(text, i);
                terminal.Write(glyph); i += glyph.Length - 1;
            }
        }
        return terminal.Result(title);
    }

    private sealed class Row(int width)
    {
        public string[] Text { get; } = Enumerable.Repeat(" ", width).ToArray();
        public uint[] Foreground { get; } = Enumerable.Repeat(Palette[7], width).ToArray();
        public uint[] Background { get; } = Enumerable.Repeat(Palette[0], width).ToArray();
    }

    private sealed class Terminal(int width, bool ice)
    {
        private readonly List<Row> rows = [new(width)];
        private int foreground = 7, background, savedX, savedY;
        private uint? trueForeground, trueBackground;
        private bool bold, blink, reverse, conceal, wide;
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Ignored, Incomplete;
        private uint Foreground => conceal ? Background : reverse ? BaseBackground : BaseForeground;
        private uint Background => reverse ? BaseForeground : BaseBackground;
        private uint BaseForeground => trueForeground ?? Color(foreground < 8 && bold ? foreground + 8 : foreground);
        private uint BaseBackground => trueBackground ?? Color(background < 8 && ice && blink ? background + 8 : background);
        public void Move(int x, int y)
        {
            if (y >= MaximumRows) throw new ArgumentException("ANSI 画面超过 2000 行限制，请减少输入或增加列数。");
            X = Math.Clamp(x, 0, width - 1); Y = Math.Max(0, y);
            while (rows.Count <= Y) rows.Add(new(width));
        }
        private void ClearGlyph(Row row, int column)
        {
            if (row.Text[column] == "" && column > 0) row.Text[column - 1] = " ";
            if (UnicodeGrid.GlyphWidth(row.Text[column]) == 2 && column + 1 < width) row.Text[column + 1] = " ";
            row.Text[column] = " ";
        }
        public void Write(string text)
        {
            // Defer wrapping until a printable character arrives; a CR/LF after
            // exactly one full row must not create an extra blank row.
            var columns = UnicodeGrid.GlyphWidth(text);
            if (columns == 0)
            {
                if (X > 0 && text.EnumerateRunes().Any(r => Rune.GetUnicodeCategory(r) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.EnclosingMark))
                {
                    var previous = X - 1;
                    if (rows[Y].Text[previous] == "" && previous > 0) previous--;
                    rows[Y].Text[previous] += text;
                }
                return;
            }
            if (X + columns > width) Move(0, Y + 1);
            var row = rows[Y];
            ClearGlyph(row, X);
            if (columns == 2) ClearGlyph(row, X + 1);
            row.Text[X] = text;
            for (var offset = 0; offset < columns; offset++)
            {
                if (offset > 0) row.Text[X + offset] = "";
                row.Foreground[X + offset] = Foreground; row.Background[X + offset] = Background;
            }
            X += columns;
            wide |= columns == 2;
        }
        public void Save() { savedX = X; savedY = Y; }
        public void Restore() => Move(savedX, savedY);
        public void Reset() { rows.Clear(); rows.Add(new(width)); X = Y = savedX = savedY = 0; Attributes(); }
        private void Attributes() { foreground = 7; background = 0; trueForeground = trueBackground = null; bold = blink = reverse = conceal = false; }
        private void Erase(int y, int start, int end)
        {
            var row = rows[y];
            for (var x = Math.Max(0, start); x < Math.Min(width, end); x++) { ClearGlyph(row, x); row.Foreground[x] = Foreground; row.Background[x] = Background; }
        }
        public void Command(char command, int[] parameters)
        {
            if (command != 'm') X = Math.Min(X, width - 1);
            int P(int i, int fallback = 1) => i < parameters.Length && parameters[i] != 0 ? parameters[i] : fallback;
            var n = Math.Min(P(0), MaximumRows + width);
            switch (command)
            {
                case 'm': Sgr(parameters); break;
                case 'H': case 'f': Move(P(1) - 1, P(0) - 1); break;
                case 'A': Move(X, Y - n); break;
                case 'B': case 'e': Move(X, Y + n); break;
                case 'C': case 'a': Move(Math.Min(width - 1, X + n), Y); break;
                case 'D': Move(X - n, Y); break;
                case 'E': Move(0, Y + n); break;
                case 'F': Move(0, Y - n); break;
                case 'G': case '`': Move(P(0) - 1, Y); break;
                case 'd': Move(X, P(0) - 1); break;
                case 's': Save(); break;
                case 'u': Restore(); break;
                case 'K':
                    if (parameters[0] == 0) Erase(Y, Math.Min(X, width - 1), width);
                    else if (parameters[0] == 1) Erase(Y, 0, X + 1);
                    else if (parameters[0] == 2) Erase(Y, 0, width); else Ignored++;
                    break;
                case 'J':
                    if (parameters[0] is < 0 or > 2) { Ignored++; break; }
                    for (var y = 0; y < rows.Count; y++)
                        if (parameters[0] == 2 || (parameters[0] == 0 && y > Y) || (parameters[0] == 1 && y < Y)) Erase(y, 0, width);
                    if (parameters[0] == 0) Erase(Y, Math.Min(X, width - 1), width);
                    if (parameters[0] == 1) Erase(Y, 0, X + 1);
                    break;
                default: Ignored++; break;
            }
        }
        private void Sgr(int[] parameters)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                var code = parameters[i];
                if (code == 0) Attributes();
                else if (code == 1) bold = true;
                else if (code == 22) bold = false;
                else if (code is 5 or 6) { blink = true; if (!ice) Ignored++; }
                else if (code == 25) blink = false;
                else if (code == 7) reverse = true;
                else if (code == 27) reverse = false;
                else if (code == 8) conceal = true;
                else if (code == 28) conceal = false;
                else if (code is >= 30 and <= 37 or >= 90 and <= 97) { foreground = code >= 90 ? code - 90 + 8 : code - 30; trueForeground = null; }
                else if (code is >= 40 and <= 47 or >= 100 and <= 107) { background = code >= 100 ? code - 100 + 8 : code - 40; trueBackground = null; }
                else if (code == 39) { foreground = 7; trueForeground = null; }
                else if (code == 49) { background = 0; trueBackground = null; }
                else if (code is 38 or 48)
                {
                    uint? color = null;
                    if (i + 2 < parameters.Length && parameters[i + 1] == 5) { var index = parameters[i + 2]; if (index is >= 0 and <= 255) color = Color(index); i += 2; }
                    else if (i + 4 < parameters.Length && parameters[i + 1] == 2)
                    {
                        var r = parameters[i + 2]; var g = parameters[i + 3]; var b = parameters[i + 4];
                        if (r is >= 0 and <= 255 && g is >= 0 and <= 255 && b is >= 0 and <= 255) color = 0xFF000000 | (uint)(r << 16 | g << 8 | b);
                        i += 4;
                    }
                    if (color is null) { Ignored++; break; }
                    if (code == 38) trueForeground = color; else trueBackground = color;
                }
                else Ignored++;
            }
        }
        public AnsiResult Result(string title)
        {
            var document = new AsciiDocument
            {
                GridVersion = 1,
                Width = width,
                Height = rows.Count,
                Title = title,
                Text = string.Join('\n', rows.Select(r => string.Concat(r.Text))),
                Colors = rows.SelectMany(r => r.Foreground).ToArray(),
                BackgroundColors = rows.SelectMany(r => r.Background).ToArray()
            };
            document.Validate(); return new(document, Ignored, Incomplete, wide);
        }
    }

    private static uint Color(int index)
    {
        if (index < 16) return Palette[index];
        if (index >= 232) { var gray = (uint)(8 + (index - 232) * 10); return 0xFF000000 | gray << 16 | gray << 8 | gray; }
        index -= 16; int Channel(int value) => value == 0 ? 0 : 55 + value * 40;
        return 0xFF000000 | (uint)(Channel(index / 36) << 16 | Channel(index / 6 % 6) << 8 | Channel(index % 6));
    }
}
