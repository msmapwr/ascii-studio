using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Charloom.Services;

/// <summary>Desktop labels only. Artwork, identifiers and paths must never be translated.</summary>
public static class GuiText
{
    private static readonly IReadOnlyDictionary<string, string> english = Load();
    private static readonly (Regex Pattern, string Prefix, string English)[] templates = english
        .Where(pair => Regex.IsMatch(pair.Key, @"\{\d+\}"))
        .Select(pair => (
            new Regex("\\A" + Regex.Replace(Regex.Escape(pair.Key), @"\\\{\d+}", "(.*?)") + "\\z",
                RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50)),
            pair.Key[..pair.Key.IndexOf('{')], pair.Value)).ToArray();

    public static string ResolveLanguage(string? preference, CultureInfo? systemCulture = null) => preference switch
    {
        "zh-CN" => "zh-CN", "en-US" => "en-US",
        _ => (systemCulture ?? CultureInfo.CurrentUICulture).Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US"
    };

    public static string Translate(string text, string? preference = null)
    {
        if (ResolveLanguage(preference ?? WorkspaceService.Settings.UiLanguage) == "zh-CN" || text.Length == 0) return text;
        if (english.TryGetValue(text, out var literal)) return literal;
        // Status labels contain numbers and names. Only anchored, catalogued templates
        // are eligible; captures (including file names) are copied without translation.
        if (text.Length <= 16_384)
            foreach (var template in templates)
            {
                if (!text.StartsWith(template.Prefix, StringComparison.Ordinal)) continue;
                var match = template.Pattern.Match(text);
                if (match.Success) return string.Format(CultureInfo.CurrentCulture, template.English,
                    match.Groups.Cast<Group>().Skip(1).Select(group => (object)group.Value).ToArray());
            }
        return text;
    }

    public static string Format(string chinese, params object?[] values) => string.Format(CultureInfo.CurrentCulture,
        ResolveLanguage(WorkspaceService.Settings.UiLanguage) == "en-US" && english.TryGetValue(chinese, out var value) ? value : chinese, values);

    private static IReadOnlyDictionary<string, string> Load()
    {
        using var stream = typeof(GuiText).Assembly.GetManifestResourceStream("Charloom.Gui.en-US.json")
            ?? throw new InvalidOperationException("Missing desktop language catalog.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}
