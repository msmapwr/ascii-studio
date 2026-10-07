using System.Globalization;
using Charloom.Services;
using Xunit;

namespace Charloom.Creation.Tests;

[Collection("Workspace")]
public sealed class GuiTextTests
{
    [Fact]
    public void ExplicitAndSystemLanguagesResolveWithoutChangingTheProcessCulture()
    {
        var original = CultureInfo.CurrentUICulture;
        Assert.Equal("zh-CN", GuiText.ResolveLanguage("system", CultureInfo.GetCultureInfo("zh-TW")));
        Assert.Equal("en-US", GuiText.ResolveLanguage("system", CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("en-US", GuiText.ResolveLanguage("en-US", CultureInfo.GetCultureInfo("zh-CN")));
        Assert.Equal("zh-CN", GuiText.ResolveLanguage("zh-CN", CultureInfo.GetCultureInfo("en-US")));
        Assert.Same(original, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void CatalogTranslatesLabelsAndStatusesWhilePreservingCapturedNamesAndUnknownText()
    {
        Assert.Equal("Image conversion", GuiText.Translate("图片转换", "en-US"));
        Assert.Equal("Encryption and encoding", GuiText.Translate("加密与编码", "en-US"));
        Assert.Equal("Tutorial", GuiText.Translate("使用教程", "en-US"));
        Assert.Equal("图片转换", GuiText.Translate("图片转换", "zh-CN"));
        Assert.Equal("Showing 2 / 18 settings", GuiText.Translate("显示 2 / 18 项设置", "en-US"));
        Assert.Equal("Cache 1.5 MB · 3 reused stages", GuiText.Translate("缓存 1.5 MB · 累计复用 3 个阶段", "en-US"));
        Assert.Equal("Exported PNG: 设置.png", GuiText.Translate("已导出 PNG：设置.png", "en-US"));
        Assert.Equal("32 × 8 · 256 characters", GuiText.Translate("32 × 8 · 256 字符", "en-US"));
        Assert.Equal("Visible region 80 × 24 px · character preview", GuiText.Translate("可见区域 80 × 24 px · 字符预览", "en-US"));
        Assert.Equal("用户作品中包含图片转换和设置", GuiText.Translate("用户作品中包含图片转换和设置", "en-US"));
        Assert.Equal("C:\\作品\\设置.png", GuiText.Translate("C:\\作品\\设置.png", "en-US"));
    }

    [Fact]
    public async Task LanguagePreferencesRoundTripAndOlderPreferencesUseSystemDefault()
    {
        await WorkspaceService.SetSettings(new(UiLanguage: "en-US", RecentFiles: ["private.asciiproj"]));
        var exported = WorkspaceService.ExportPreferences();
        Assert.Equal("Settings", GuiText.Translate("设置"));
        await WorkspaceService.SetSettings(WorkspaceService.Settings with { UiLanguage = "zh-CN" });
        Assert.Equal("设置", GuiText.Translate("设置"));
        await WorkspaceService.ImportPreferences(exported);
        Assert.Equal("en-US", WorkspaceService.Settings.UiLanguage);
        Assert.Equal(["private.asciiproj"], Assert.IsType<string[]>(WorkspaceService.Settings.RecentFiles));
        await WorkspaceService.ImportPreferences("{}"u8.ToArray());
        Assert.Equal("system", WorkspaceService.Settings.UiLanguage);
        await WorkspaceService.SetSettings(new(UiLanguage: "invalid"));
        Assert.Equal("system", WorkspaceService.Settings.UiLanguage);
    }
}
