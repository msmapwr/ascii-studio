using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AsciiStudio.Core;

public static class TextUtilities
{
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    public static string TrimCanvas(string text)
    {
        var rows = Normalize(text).Split('\n').ToList();
        while (rows.Count > 0 && string.IsNullOrWhiteSpace(rows[0])) rows.RemoveAt(0);
        while (rows.Count > 0 && string.IsNullOrWhiteSpace(rows[^1])) rows.RemoveAt(rows.Count - 1);
        if (rows.Count == 0) return "";
        var left = rows.Where(r => !string.IsNullOrWhiteSpace(r)).Min(r => r.TakeWhile(c => c == ' ').Count());
        return string.Join('\n', rows.Select(r => (r.Length >= left ? r[left..] : "").TrimEnd(' ', '\t')));
    }

    public static string Analyze(string text)
    {
        var normalized = Normalize(text);
        var rows = normalized.Split('\n');
        var runes = normalized.EnumerateRunes().ToArray();
        var nonAscii = runes.Where(r => r.Value > 127).ToArray();
        var graphemes = new StringInfo(normalized).LengthInTextElements;
        var controls = runes.Count(r => Rune.IsControl(r) && r.Value is not 10 and not 9);
        return $"字符分析\n========\nUnicode 标量：{runes.Length:N0}\n可见字符簇（含换行）：{graphemes:N0}\nUTF-16 单元：{text.Length:N0}\nUTF-8 字节：{Encoding.UTF8.GetByteCount(text):N0}\n行数：{rows.Length:N0}\n最长行（字符簇）：{rows.Max(r => new StringInfo(r).LengthInTextElements):N0}\n词数（空白分隔）：{Regex.Matches(text, @"\S+").Count:N0}\n非 ASCII 字符：{nonAscii.Length:N0}\n其他控制字符：{controls:N0}\n制表符：{text.Count(c => c == '\t'):N0}\n\nASCII 校验：{(nonAscii.Length == 0 && controls == 0 ? "通过" : "包含非 ASCII 或控制字符")}\n" +
            (nonAscii.Length == 0 ? "" : "\n非 ASCII 示例：\n" + string.Join('\n', nonAscii.Distinct().Take(30).Select(r => $"{r}  U+{r.Value:X4}")));
    }

    public static string Clean(string text, bool asciiOnly)
    {
        var b = new StringBuilder();
        foreach (var r in Normalize(text).EnumerateRunes())
            if ((!Rune.IsControl(r) || r.Value is 10 or 9) && (!asciiOnly || r.Value < 128)) b.Append(r);
        return b.ToString();
    }
}
