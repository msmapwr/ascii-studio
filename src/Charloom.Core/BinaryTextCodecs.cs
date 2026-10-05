using System.Numerics;
using System.Text;

namespace Charloom.Core;

internal static class BinaryTextCodecs
{
    private const string Base32Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUV";
    private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    private const int Base58ByteLimit = 16_384;
    public static string Apply(string name, string text, bool reverse)
    {
        if (!reverse)
        {
            var data = TextProcessing.StrictUtf8.GetBytes(text);
            return name switch
            {
                "Base32hex" => Base32Encode(data), "Base58" => Base58Encode(data),
                "Ascii85" => Ascii85Encode(data), "Quoted-printable" => QuotedPrintableEncode(data),
                _ => throw new ArgumentException("未知字节编码。")
            };
        }
        var bytes = name switch
        {
            "Base32hex" => Base32Decode(text), "Base58" => Base58Decode(text),
            "Ascii85" => Ascii85Decode(text), "Quoted-printable" => QuotedPrintableDecode(text),
            _ => throw new ArgumentException("未知字节编码。")
        };
        return TextProcessing.StrictUtf8.GetString(bytes);
    }
    private static string Base32Encode(byte[] bytes)
    {
        var result = new StringBuilder(); uint buffer = 0; var bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { bits -= 5; result.Append(Base32Alphabet[(int)(buffer >> bits) & 31]); }
        }
        if (bits > 0) result.Append(Base32Alphabet[(int)(buffer << (5 - bits)) & 31]);
        while (result.Length % 8 != 0) result.Append('=');
        return result.ToString();
    }
    private static byte[] Base32Decode(string text)
    {
        text = text.ToUpperInvariant();
        var content = text.TrimEnd('='); var remainder = content.Length % 8;
        if (remainder is 1 or 3 or 6 || (content.Length != text.Length && (text.Length % 8 != 0 || text.Length - content.Length != (8 - remainder) % 8))) throw new FormatException("Base32hex 填充或长度无效。");
        using var result = new MemoryStream(); uint buffer = 0; var bits = 0;
        foreach (var c in content)
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0) throw new FormatException("Base32hex 字符无效。");
            buffer = (buffer << 5) | (uint)index; bits += 5;
            if (bits >= 8) { bits -= 8; result.WriteByte((byte)(buffer >> bits)); }
        }
        if ((buffer & ((1u << bits) - 1)) != 0) throw new FormatException("Base32hex 尾部位无效。");
        return result.ToArray();
    }
    private static string Base58Encode(byte[] bytes)
    {
        if (bytes.Length > Base58ByteLimit) throw new ArgumentException("Base58 适合短文本，输入最多 16 KB。");
        var zeros = bytes.TakeWhile(b => b == 0).Count();
        var number = new BigInteger(bytes, true, true); var result = new StringBuilder();
        while (number > 0) { number = BigInteger.DivRem(number, 58, out var remainder); result.Append(Base58Alphabet[(int)remainder]); }
        result.Append('1', zeros);
        return new string(result.ToString().Reverse().ToArray());
    }
    private static byte[] Base58Decode(string text)
    {
        if (text.Length > Base58ByteLimit * 2) throw new ArgumentException("Base58 输入过长，解码最多 16 KB。");
        var number = BigInteger.Zero;
        foreach (var c in text)
        {
            var value = Base58Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException("Base58 字符无效（不包含 0、O、I、l）。");
            number = number * 58 + value;
        }
        var zeros = text.TakeWhile(c => c == '1').Count();
        var body = number.IsZero ? [] : number.ToByteArray(true, true);
        if (zeros + body.Length > Base58ByteLimit) throw new ArgumentException("Base58 解码最多 16 KB。");
        return [.. new byte[zeros], .. body];
    }
    private static string Ascii85Encode(byte[] data)
    {
        var result = new StringBuilder("<~");
        for (var i = 0; i < data.Length; i += 4)
        {
            var count = Math.Min(4, data.Length - i); uint value = 0;
            for (var j = 0; j < 4; j++) value = (value << 8) | (j < count ? data[i + j] : 0u);
            if (value == 0 && count == 4) { result.Append('z'); continue; }
            var group = new char[5];
            for (var j = 4; j >= 0; j--) { group[j] = (char)('!' + value % 85); value /= 85; }
            result.Append(group, 0, count + 1);
        }
        return result.Append("~>").ToString();
    }
    private static byte[] Ascii85Decode(string text)
    {
        text = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        if (text.StartsWith("<~", StringComparison.Ordinal))
        {
            if (!text.EndsWith("~>", StringComparison.Ordinal)) throw new FormatException("Ascii85 缺少结束符 ~>。");
            text = text[2..^2];
        }
        using var result = new MemoryStream(); ulong value = 0; var count = 0;
        void Flush(int actualCount)
        {
            if (value > uint.MaxValue) throw new FormatException("Ascii85 分组溢出。");
            for (var j = 0; j < actualCount - 1; j++) result.WriteByte((byte)(value >> (24 - 8 * j)));
            value = 0; count = 0;
        }
        foreach (var c in text)
        {
            if (c == 'z')
            {
                if (count != 0) throw new FormatException("Ascii85 z 只能替代完整分组。");
                result.Write(new byte[4]); continue;
            }
            if (c is < '!' or > 'u') throw new FormatException("Ascii85 字符无效。");
            value = value * 85 + (uint)(c - '!'); count++;
            if (count == 5) Flush(5);
        }
        if (count == 1) throw new FormatException("Ascii85 尾部不完整。");
        if (count > 1)
        {
            var actual = count;
            while (count++ < 5) value = value * 85 + 84;
            Flush(actual);
        }
        return result.ToArray();
    }
    private static string QuotedPrintableEncode(byte[] data)
    {
        var result = new StringBuilder(); var column = 0;
        foreach (var b in data)
        {
            // Encode all whitespace/newlines to preserve arbitrary text exactly.
            var token = b is >= 33 and <= 126 && b != '=' ? ((char)b).ToString() : $"={b:X2}";
            if (column + token.Length > 75) { TextProcessing.AppendBounded(result, "=\r\n"); column = 0; }
            TextProcessing.AppendBounded(result, token); column += token.Length;
        }
        return result.ToString();
    }
    private static byte[] QuotedPrintableDecode(string text)
    {
        using var result = new MemoryStream();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '=')
            {
                if (c > 127 || (c < 32 && c is not ('\r' or '\n' or '\t'))) throw new FormatException("Quoted-printable 必须使用 ASCII 字符。");
                result.WriteByte((byte)c); continue;
            }
            if (i + 2 < text.Length && text[i + 1] == '\r' && text[i + 2] == '\n') { i += 2; continue; }
            if (i + 2 >= text.Length || !byte.TryParse(text.AsSpan(i + 1, 2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var b)) throw new FormatException("Quoted-printable 转义无效。");
            result.WriteByte(b); i += 2;
        }
        return result.ToArray();
    }
}
