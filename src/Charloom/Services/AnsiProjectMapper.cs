using Charloom.Core;

namespace Charloom.Services;

public sealed record AnsiProjectState(string Encoding, int Columns, bool Ice, byte[]? Bytes, string Text, string Title);

public static class AnsiProjectMapper
{
    public static AnsiProjectState Restore(StudioProject project)
    {
        var p = project.Parameters;
        var encoding = p?.GetValueOrDefault("encoding") ?? "Auto";
        if (encoding is not ("Auto" or "UTF-8" or "CP437")) throw new ArgumentException("项目 ANSI 编码无效。");
        var columns = int.TryParse(p?.GetValueOrDefault("columns"), out var width) && width is >= 20 and <= 300 ? width : 80;
        var ice = bool.TryParse(p?.GetValueOrDefault("ice"), out var useIce) && useIce;
        var encoded = p?.GetValueOrDefault("bytes");
        if (encoded?.Length > (AnsiArt.InputLimit + 2L) / 3 * 4) throw new ArgumentException("项目的 ANSI 来源超过 4 MB 限制。");
        var bytes = string.IsNullOrEmpty(encoded) ? null : Convert.FromBase64String(encoded);
        if (bytes?.Length > AnsiArt.InputLimit) throw new ArgumentException("项目的 ANSI 来源超过 4 MB 限制。");
        var text = project.SourceText ?? "";
        if (text.Length > AnsiArt.InputLimit || new System.Text.UTF8Encoding(false, true).GetByteCount(text) > AnsiArt.InputLimit)
            throw new ArgumentException("项目的 ANSI 原文超过 4 MB 限制。");
        return new(encoding, columns, ice, bytes, text, project.Document.Title);
    }
}
