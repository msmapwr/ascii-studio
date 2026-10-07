using System.Globalization;

namespace Charloom.Cli;

/// <summary>Stable parser diagnostics; rejected configuration values are never copied into messages.</summary>
internal static class CliDiagnostics
{
    private static readonly Dictionary<string, (string English, string Chinese)> usage = new(StringComparer.Ordinal)
    {
        ["data_directory_conflict"] = ("--desktop-data conflicts with --data-directory.", "--desktop-data 与 --data-directory 不能同时使用。"),
        ["missing_option"] = ("Missing --{0}.", "缺少 --{0}。"),
        ["invalid_boolean"] = ("--{0} expects true or false.", "--{0} 需要 true 或 false。"),
        ["invalid_integer"] = ("--{0} expects an integer.", "--{0} 需要整数。"),
        ["invalid_number"] = ("--{0} expects a finite number.", "--{0} 需要有限数值。"),
        ["unknown_option"] = ("Unknown option: {0}", "未知选项：{0}"),
        ["missing_option_value"] = ("Missing value for --{0}.", "--{0} 缺少值。"),
        ["duplicate_option"] = ("Duplicate option: --{0}", "重复选项：--{0}"),
        ["invalid_language"] = ("--language: system | zh-CN | en-US", "--language 支持 system | zh-CN | en-US。"),
        ["unknown_command"] = ("Unknown command. Use --help.", "未知命令，请使用 --help。"),
        ["unsupported_option"] = ("--{0} is not supported by {1}.", "{1} 不支持 --{0}。"),
        ["configuration_object"] = ("Configuration must be an object.", "配置必须为对象。"),
        ["configuration_null"] = ("Configuration cannot be null.", "配置不能为空。"),
        ["invalid_assignment"] = ("--set expects Name=Value or group.Name=Value.", "--set 需要 Name=Value 或 group.Name=Value。"),
        ["unknown_property"] = ("Unknown {0} property: {1}", "未知 {0} 属性：{1}"),
        ["readonly_setting"] = ("{0} cannot be changed using --set.", "不能使用 --set 修改 {0}。"),
        ["unsupported_assignment"] = ("Unsupported --set assignment; check the property and group name.", "不支持此 --set 赋值，请检查属性和分组名称。"),
        ["invalid_value"] = ("Invalid {0} value.", "无效的 {0} 值。")
    };

    public static CliUsageException Usage(string code, params object[] arguments)
    {
        var text = usage[code];
        return new(string.Format(CultureInfo.InvariantCulture, text.English, arguments),
            string.Format(CultureInfo.InvariantCulture, text.Chinese, arguments), code);
    }

    // Parse can fail before returning arguments. Read only the diagnostic switches,
    // including switches after the failure; never reparse the rejected invocation.
    public static (bool Chinese, bool Json) Options(string[] raw)
    {
        var language = "system"; var json = false;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == "--language") language = i + 1 < raw.Length && !raw[i + 1].StartsWith("--", StringComparison.Ordinal) ? raw[++i] : "system";
            else if (raw[i].StartsWith("--language=", StringComparison.Ordinal)) language = raw[i][11..];
            else if (raw[i] == "--json") json = true;
            else if (raw[i].StartsWith("--json=", StringComparison.Ordinal)) json = bool.TryParse(raw[i][7..], out var value) && value;
        }
        return (language switch { "zh-CN" => true, "en-US" => false,
            _ => CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) }, json);
    }
}
