using System.Globalization;
using System.Text.Json;
using Charloom.Cli;
using Xunit;

namespace Charloom.Cli.Tests;

[Collection("CLI")]
public sealed class CliDiagnosticsTests
{
    private static async Task<(int Exit, string Output, string Error)> Run(params string[] arguments)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await CliHost.Run(arguments, output, error, TextReader.Null);
        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task ParseFailuresHonorLanguageAfterTheErrorAndKeepStableCodes()
    {
        foreach (var (arguments, detail, english, chinese) in new[]
        {
            (new[] { "--unknown" }, "unknown_option", "Unknown option", "未知选项"),
            (new[] { "settings", "show", "--quiet=bad" }, "invalid_boolean", "expects true or false", "需要 true 或 false"),
            (new[] { "text", "--text" }, "missing_option_value", "Missing value", "缺少值"),
            (new[] { "text", "--text", "a", "--text", "b" }, "duplicate_option", "Duplicate option", "重复选项"),
            (new[] { "not-a-command" }, "unknown_command", "Unknown command", "未知命令"),
            (new[] { "fonts", "list", "--output", "unused" }, "unsupported_option", "not supported", "不支持")
        })
        {
            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                var result = await Run([.. arguments, "--language=" + language, "--json"]);
                Assert.Equal(2, result.Exit); Assert.Empty(result.Output);
                using var json = JsonDocument.Parse(result.Error);
                Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("usage", json.RootElement.GetProperty("code").GetString());
                Assert.Equal(detail, json.RootElement.GetProperty("detailCode").GetString());
                Assert.Contains(language == "zh-CN" ? chinese : english, json.RootElement.GetProperty("message").GetString());
            }
        }
    }

    [Fact]
    public async Task ConfigurationFailuresNeverEchoRejectedValues()
    {
        const string secret = "PRIVATE-PASSWORD-KEEP-OUT";
        foreach (var (assignment, detail) in new[]
        {
            ("UiFontSize=" + secret, "invalid_value"),
            ("unexpected.Value=" + secret, "unsupported_assignment"),
            (secret, "unsupported_assignment")
        })
        {
            var result = await Run("settings", "set", "--set", assignment, "--language", "zh-CN", "--json");
            Assert.Equal(2, result.Exit); Assert.Empty(result.Output); Assert.DoesNotContain(secret, result.Error);
            using var json = JsonDocument.Parse(result.Error);
            Assert.Equal(detail, json.RootElement.GetProperty("detailCode").GetString());
            Assert.Contains(detail == "invalid_value" ? "无效" : "不支持", json.RootElement.GetProperty("message").GetString());
        }
    }

    [Fact]
    public async Task InvalidDiagnosticSwitchesCannotBreakErrorReportingOrChangeCulture()
    {
        var oldCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-TW");
            var malformed = await Run("--json=bad", "--language", "zh-CN");
            Assert.Equal(2, malformed.Exit); Assert.StartsWith("usage: --json 需要", malformed.Error);
            var missing = await Run("--language", "--json");
            Assert.Equal(2, missing.Exit);
            using var json = JsonDocument.Parse(missing.Error);
            Assert.Equal("missing_option_value", json.RootElement.GetProperty("detailCode").GetString());
            Assert.Contains("缺少", json.RootElement.GetProperty("message").GetString());
            var english = await Run("--unknown", "--language", "en-US");
            Assert.StartsWith("usage: Unknown option", english.Error);
            Assert.Equal("zh-TW", CultureInfo.CurrentUICulture.Name);
        }
        finally { CultureInfo.CurrentUICulture = oldCulture; }
    }
}
