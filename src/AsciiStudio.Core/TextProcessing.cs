using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace AsciiStudio.Core;

public sealed record TextProcessingOptions(bool Base64Bytes = false, bool IncludeBom = false);

/// <summary>Text-only tools; byte representations are explicit and decoding is strict.</summary>
public static class TextProcessing
{
    public static readonly string[] CharacterEncodings = ["ASCII", "UTF-8", "UTF-16LE", "UTF-16BE", "GB18030", "GBK", "Big5", "Shift_JIS"];
    public static readonly string[] Representations = ["Unicode \\uXXXX", "Unicode \\UXXXXXXXX", "HTML 十进制实体", "HTML 十六进制实体", "JSON 转义", "NFC", "NFKC", "Punycode"];
    public static readonly string[] BinaryEncodings = ["Base32hex", "Base58", "Ascii85", "Quoted-printable"];
    public static readonly string[] Compression = ["GZIP", "Zlib", "Brotli"];
    public static readonly string[] Checksums = ["CRC32", "Adler-32"];
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);

    static TextProcessing() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static bool Contains(string name) => CharacterEncodings.Contains(name) || Representations.Contains(name) || BinaryEncodings.Contains(name) || Compression.Contains(name) || Checksums.Contains(name);
    public static bool CanReverse(string name) => name is not ("NFC" or "NFKC") && !Checksums.Contains(name);
    public static string Apply(string name, string text, bool reverse, TextProcessingOptions? options = null)
    {
        options ??= new();
        if (text.Length > CryptoTools.OutputLimit || StrictUtf8.GetByteCount(text) > (reverse ? CryptoTools.OutputLimit : CryptoTools.InputLimit))
            throw new ArgumentException("输入最多 1 MB，还原输入最多 4 MB。");
        if (reverse && !CanReverse(name)) throw new ArgumentException("规范化和校验不可还原。");
        var result = CharacterEncodings.Contains(name) ? CharacterEncoding(name, text, reverse, options)
            : BinaryEncodings.Contains(name) ? BinaryTextCodecs.Apply(name, text, reverse)
            : Compression.Contains(name) ? Compress(name, text, reverse)
            : Checksums.Contains(name) ? Checksum(name, StrictUtf8.GetBytes(text))
            : Represent(name, text, reverse);
        if (result.Length > CryptoTools.OutputLimit) throw new ArgumentException("结果超过 4 MB，请减少输入。");
        // Also catches isolated surrogate code units produced by malformed escapes.
        _ = StrictUtf8.GetByteCount(result);
        return result;
    }
    private static Encoding GetEncoding(string name, bool bom) => name switch
    {
        "UTF-8" => new UTF8Encoding(bom, true),
        "UTF-16LE" => new UnicodeEncoding(false, bom, true),
        "UTF-16BE" => new UnicodeEncoding(true, bom, true),
        _ => Encoding.GetEncoding(name switch { "ASCII" => 20127, "GB18030" => 54936, "GBK" => 936, "Big5" => 950, "Shift_JIS" => 932, _ => throw new ArgumentException("未知字符编码。") }, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
    };
    private static string CharacterEncoding(string name, string text, bool reverse, TextProcessingOptions options)
    {
        var encoding = GetEncoding(name, options.IncludeBom);
        if (!reverse)
        {
            byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
            return options.Base64Bytes ? Convert.ToBase64String(bytes) : Convert.ToHexString(bytes);
        }
        var data = options.Base64Bytes ? Convert.FromBase64String(text) : Convert.FromHexString(string.Concat(text.Where(c => !char.IsWhiteSpace(c))));
        var preamble = GetEncoding(name, true).GetPreamble();
        var offset = preamble.Length > 0 && data.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
        return encoding.GetString(data, offset, data.Length - offset);
    }
    private static string Represent(string name, string text, bool reverse)
    {
        if (name == "NFC") return text.Normalize(NormalizationForm.FormC);
        if (name == "NFKC") return text.Normalize(NormalizationForm.FormKC);
        if (name == "Punycode") return PunycodeCodec.Apply(text, reverse);
        if (name == "JSON 转义") return reverse ? JsonSerializer.Deserialize<string>(text) ?? throw new FormatException("需要 JSON 字符串，不能为 null。") : JsonSerializer.Serialize(text);
        if (reverse) return DecodeEscapes(name, text);
        var result = new StringBuilder();
        if (name == "Unicode \\uXXXX")
            foreach (var c in text) AppendBounded(result, $"\\u{(int)c:X4}");
        else foreach (var rune in text.EnumerateRunes())
            AppendBounded(result, name switch
            {
                "Unicode \\UXXXXXXXX" => $"\\U{rune.Value:X8}",
                "HTML 十进制实体" => $"&#{rune.Value};",
                "HTML 十六进制实体" => $"&#x{rune.Value:X};",
                _ => throw new ArgumentException("未知表示方法。")
            });
        return result.ToString();
    }
    private static string DecodeEscapes(string name, string text)
    {
        var result = new StringBuilder();
        for (var i = 0; i < text.Length;)
        {
            var unicode = name.StartsWith("Unicode ", StringComparison.Ordinal);
            if (unicode && text[i] == '\\')
            {
                var shortForm = name == "Unicode \\uXXXX";
                var digits = shortForm ? 4 : 8;
                if (i + digits + 2 > text.Length || text[i + 1] != (shortForm ? 'u' : 'U') ||
                    !uint.TryParse(text.AsSpan(i + 2, digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) throw new FormatException("Unicode 转义无效。");
                if (shortForm) result.Append((char)value);
                else if (value <= int.MaxValue && Rune.IsValid((int)value)) result.Append(new Rune((int)value));
                else throw new FormatException("无效的 Unicode 码点。");
                i += digits + 2;
            }
            else if (!unicode && text[i] == '&')
            {
                var end = text.IndexOf(';', i + 1);
                var hex = name == "HTML 十六进制实体";
                var prefix = hex ? "&#x" : "&#";
                if (end < 0 || end - i < prefix.Length || end - i > 12 || !text.AsSpan(i).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(text.AsSpan(i + prefix.Length, end - i - prefix.Length), hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out var value) || !Rune.IsValid(value))
                    throw new FormatException("无效的数字实体或 Unicode 码点。");
                result.Append(new Rune(value)); i = end + 1;
            }
            else result.Append(text[i++]);
        }
        return result.ToString();
    }
    internal static void AppendBounded(StringBuilder result, string value)
    {
        if ((long)result.Length + value.Length > CryptoTools.OutputLimit) throw new ArgumentException("编码结果超过 4 MB。");
        result.Append(value);
    }
    private static Stream CompressionStream(string name, Stream stream, bool reverse) => (name, reverse) switch
    {
        ("GZIP", true) => new GZipStream(stream, CompressionMode.Decompress, true),
        ("GZIP", false) => new GZipStream(stream, CompressionLevel.Optimal, true),
        ("Zlib", true) => new ZLibStream(stream, CompressionMode.Decompress, true),
        ("Zlib", false) => new ZLibStream(stream, CompressionLevel.Optimal, true),
        ("Brotli", true) => new BrotliStream(stream, CompressionMode.Decompress, true),
        ("Brotli", false) => new BrotliStream(stream, CompressionLevel.Optimal, true),
        _ => throw new ArgumentException("未知压缩方法。")
    };
    private static string Compress(string name, string text, bool reverse)
    {
        using var result = new MemoryStream();
        if (!reverse)
        {
            using (var compressor = CompressionStream(name, result, false)) compressor.Write(StrictUtf8.GetBytes(text));
            return Convert.ToBase64String(result.ToArray());
        }
        using var input = new MemoryStream(Convert.FromBase64String(text));
        using var decompressor = CompressionStream(name, input, true);
        var buffer = new byte[8192];
        int count;
        while ((count = decompressor.Read(buffer)) != 0)
        {
            if (result.Length + count > CryptoTools.OutputLimit) throw new InvalidDataException("解压结果超过 4 MB 上限。");
            result.Write(buffer, 0, count);
        }
        return StrictUtf8.GetString(result.ToArray());
    }
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(value =>
    {
        var crc = (uint)value;
        for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        return crc;
    }).ToArray();
    private static string Checksum(string name, byte[] data)
    {
        uint value;
        if (name == "CRC32")
        {
            value = uint.MaxValue;
            foreach (var b in data) value = CrcTable[(value ^ b) & 255] ^ (value >> 8);
            value ^= uint.MaxValue;
        }
        else
        {
            uint a = 1, b = 0;
            foreach (var c in data) { a = (a + c) % 65521; b = (b + a) % 65521; }
            value = (b << 16) | a;
        }
        return value.ToString("x8", CultureInfo.InvariantCulture);
    }
}
