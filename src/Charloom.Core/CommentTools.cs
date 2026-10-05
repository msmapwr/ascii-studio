namespace Charloom.Core;

public sealed record CommentLanguage(string Name, string? Line, string? Open = null, string? Close = null);

public static class CommentTools
{
    public static IReadOnlyList<CommentLanguage> Languages { get; } = [
        .. new[] { "C", "C++", "C#", "Java", "JavaScript", "TypeScript", "Rust", "Go", "Swift", "Kotlin", "Dart", "PHP", "F#", "GLSL" }.Select(n => new CommentLanguage(n, "//", "/*", "*/")),
        .. new[] { "Python", "Ruby", "Shell", "PowerShell", "R", "Perl", "YAML", "TOML", "Makefile", "Julia" }.Select(n => new CommentLanguage(n, "#")),
        new("SQL", "--", "/*", "*/"), new("Lua", "--", "--[[", "]]"), new("Haskell", "--", "{-", "-}"), new("Ada", "--"),
        new("HTML", null, "<!--", "-->"), new("XML", null, "<!--", "-->"), new("CSS", null, "/*", "*/"),
        new("VB / VBA", "'"), new("Batch", "REM"), new("MATLAB", "%"), new("LaTeX", "%"), new("Fortran", "!"),
        new("Lisp", ";"), new("Assembly", ";"), new("INI", ";"), new("Pascal", "//", "{", "}"), new("OCaml", null, "(*", "*)")
    ];

    public static string Wrap(string text, string language, bool block = false)
    {
        var syntax = Languages.FirstOrDefault(l => l.Name == language) ?? throw new ArgumentException("未知注释语言。");
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (!block && syntax.Line is not null)
        {
            var wrapped = string.Join('\n', text.Split('\n').Select(line => syntax.Line + " " + line));
            AsciiDocument.FromText(wrapped).Validate(); return wrapped;
        }
        if (syntax.Open is null || syntax.Close is null) throw new ArgumentException("该语言仅支持行注释。");
        if (text.Contains(syntax.Close, StringComparison.Ordinal) || ((language is "HTML" or "XML") && text.Contains("--", StringComparison.Ordinal)))
            throw new ArgumentException("内容包含块注释结束符或非法注释字符，请选择行注释或其他语言。");
        var result = syntax.Open + "\n" + text + "\n" + syntax.Close;
        AsciiDocument.FromText(result).Validate();
        return result;
    }
}
