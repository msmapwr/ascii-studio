using Charloom.Cli;
using Charloom.Services;
using Xunit;

namespace Charloom.Cli.Tests;

[Collection("CLI")]
public sealed class BrandIdentityTests
{
    [Fact]
    public void HelpUsesNewBrandInBothLanguages()
    {
        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var help = CliCatalog.Help(CliArguments.Parse(["--help", "--language", language]));
            Assert.Contains("Charloom CLI", help); Assert.Contains("charloom-cli", help);
            Assert.Contains(language == "zh-CN" ? "字织" : "Offline ASCII / ANSI art studio", help);
        }
    }
    [Fact]
    public void LegacyDataLocationAndEnvironmentOverridesRemainCompatible()
    {
        Assert.EndsWith(Path.DirectorySeparatorChar + "AsciiStudio", ProductIdentity.DesktopDataDirectory);
        var modern = "CHARLOOM_TEST_" + Guid.NewGuid().ToString("N"); var legacy = "ASCIISTUDIO_TEST_" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable(legacy, "legacy"); Assert.Equal("legacy", ProductIdentity.EnvironmentValue(modern, legacy));
            Environment.SetEnvironmentVariable(modern, "modern"); Assert.Equal("modern", ProductIdentity.EnvironmentValue(modern, legacy));
        }
        finally { Environment.SetEnvironmentVariable(modern, null); Environment.SetEnvironmentVariable(legacy, null); }
    }
    [Fact]
    public void NewBrandWelcomeFontIsAvailable()
    {
        Assert.True(TextFontLibrary.Available("builtin:small"));
        Assert.NotEmpty(TextFontLibrary.Render("builtin:small", ProductIdentity.Name, new()));
    }
}
