using System.Text;

namespace AsciiStudio.Core;

/// <summary>RFC 3492 raw Punycode. No IDNA mapping, case folding or xn-- prefix.</summary>
internal static class PunycodeCodec
{
    public static string Apply(string text, bool reverse)
    {
        if (text.Length > (reverse ? 16_384 : 4096)) throw new ArgumentException("Punycode 原文最多 4096 字符，编码输入最多 16 KB；不添加 xn--。");
        try { checked { return reverse ? Decode(text) : Encode(text); } }
        catch (OverflowException) { throw new FormatException("Punycode 数值溢出。"); }
    }
    private static long Adapt(long delta, long points, bool first)
    {
        delta = first ? delta / 700 : delta / 2;
        delta += delta / points; long k = 0;
        while (delta > 455) { delta /= 35; k += 36; }
        return k + 36 * delta / (delta + 38);
    }
    private static long Threshold(long k, long bias) => k <= bias ? 1 : k >= bias + 26 ? 26 : k - bias;
    private static char Digit(long value) => value < 26 ? (char)('a' + value) : (char)('0' + value - 26);
    private static int Value(char c) => c is >= 'a' and <= 'z' ? c - 'a' : c is >= 'A' and <= 'Z' ? c - 'A' : c is >= '0' and <= '9' ? c - '0' + 26 : throw new FormatException("Punycode 数字无效。");
    private static string Encode(string text)
    {
        checked
        {
            var input = text.EnumerateRunes().Select(r => r.Value).ToArray();
            var result = new StringBuilder();
            foreach (var c in input.Where(c => c < 128)) result.Append((char)c);
            var basic = result.Length; var handled = basic;
            if (basic > 0) result.Append('-');
            long n = 128, delta = 0, bias = 72;
            while (handled < input.Length)
            {
                var minimum = input.Where(c => c >= n).Min();
                delta += (minimum - n) * (handled + 1); n = minimum;
                foreach (var c in input)
                {
                    if (c < n) delta++;
                    if (c != n) continue;
                    var q = delta;
                    for (long k = 36; ; k += 36)
                    {
                        var t = Threshold(k, bias);
                        if (q < t) break;
                        result.Append(Digit(t + (q - t) % (36 - t))); q = (q - t) / (36 - t);
                    }
                    result.Append(Digit(q)); bias = Adapt(delta, handled + 1, handled == basic); delta = 0; handled++;
                }
                delta++; n++;
            }
            return result.ToString();
        }
    }
    private static string Decode(string text)
    {
        checked
        {
            var result = new List<int>(); var delimiter = text.LastIndexOf('-'); var position = 0;
            if (delimiter >= 0)
            {
                foreach (var c in text[..delimiter])
                {
                    if (c >= 128) throw new FormatException("Punycode 基础字符必须为 ASCII。");
                    result.Add(c);
                }
                position = delimiter + 1;
            }
            long n = 128, i = 0, bias = 72;
            while (position < text.Length)
            {
                var old = i; long weight = 1;
                for (long k = 36; ; k += 36)
                {
                    if (position >= text.Length) throw new FormatException("Punycode 尾部不完整。");
                    var digit = Value(text[position++]); i += digit * weight;
                    var t = Threshold(k, bias);
                    if (digit < t) break;
                    weight *= 36 - t;
                }
                bias = Adapt(i - old, result.Count + 1, old == 0);
                n += i / (result.Count + 1); i %= result.Count + 1;
                if (n > int.MaxValue || !Rune.IsValid((int)n)) throw new FormatException("Punycode 产生无效 Unicode 码点。");
                result.Insert((int)i, (int)n); i++;
            }
            return string.Concat(result.Select(c => new Rune(c).ToString()));
        }
    }
}
