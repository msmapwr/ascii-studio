using System.Text;
using System.Text.Json;
using Charloom.Services;
using Xunit;

namespace Charloom.Creation.Tests;

[Collection("Workspace")]
public sealed class SettingsCatalogTests
{
    [Fact]
    public void BilingualSearchUsesAllWordsAndFiltersCanonicalFavorites()
    {
        var settings = new StudioSettings(FavoriteSettings: ["uifontsize", "UiFontSize", "unknown", null!]);
        Assert.Equal(["UiFontSize"], SettingsCatalog.NormalizeFavorites(settings.FavoriteSettings));
        Assert.Equal(["Theme"], SettingsCatalog.Search("主题", settings).Select(entry => entry.Key));
        Assert.Equal(["UiFontFamily", "UiFontSize"], SettingsCatalog.Search("FONT appearance", settings).Select(entry => entry.Key));
        Assert.Equal(["UiFontSize"], SettingsCatalog.Search("font appearance", settings, true).Select(entry => entry.Key));
        Assert.Equal(["DefaultColumns"], SettingsCatalog.Search("DefaultColumns", settings).Select(entry => entry.Key));
        Assert.Empty(SettingsCatalog.Search("no such preference", settings));
        Assert.Throws<ArgumentException>(() => SettingsCatalog.Search(new string('x', 257), settings));
        foreach (var entry in SettingsCatalog.Entries) Assert.NotNull(typeof(StudioSettings).GetProperty(entry.Key));
        Assert.Equal(SettingsCatalog.Entries.Count, SettingsCatalog.Entries.Select(entry => entry.AutomationId).Distinct().Count());
    }

    [Fact]
    public async Task PreferencesRoundTripFavoritesKeepsRecentPathsPrivateAndResetPreservesProjects()
    {
        var initial = new StudioSettings(Theme: "Light", RecentFiles: ["private-project.asciiproj"], FavoriteSettings: ["theme", "Theme", "unknown"]);
        await WorkspaceService.SetSettings(initial);
        Assert.Equal(["Theme"], Assert.IsType<string[]>(WorkspaceService.Settings.FavoriteSettings));
        Assert.Contains("Theme", await File.ReadAllTextAsync(Path.Combine(WorkspaceService.DataDirectory, "settings.json")));
        var bytes = WorkspaceService.ExportPreferences();
        Assert.DoesNotContain("private-project.asciiproj", Encoding.UTF8.GetString(bytes));
        await WorkspaceService.ResetPreferences();
        Assert.Empty(WorkspaceService.Settings.FavoriteSettings!);
        Assert.Equal(["private-project.asciiproj"], Assert.IsType<string[]>(WorkspaceService.Settings.RecentFiles));
        await WorkspaceService.ImportPreferences(bytes);
        Assert.Equal("Light", WorkspaceService.Settings.Theme); Assert.Equal(["Theme"], Assert.IsType<string[]>(WorkspaceService.Settings.FavoriteSettings));
        Assert.Equal(["private-project.asciiproj"], Assert.IsType<string[]>(WorkspaceService.Settings.RecentFiles));
        await WorkspaceService.ImportPreferences("{}"u8.ToArray());
        Assert.Equal("Dark", WorkspaceService.Settings.Theme); Assert.Empty(WorkspaceService.Settings.FavoriteSettings!);
        Assert.Equal(["private-project.asciiproj"], Assert.IsType<string[]>(WorkspaceService.Settings.RecentFiles));
    }

    [Fact]
    public async Task InvalidImportAndExportToLiveSettingsLeaveStoredPreferencesUnchanged()
    {
        await WorkspaceService.SetSettings(new(Theme: "Light", FavoriteSettings: ["Theme"]));
        var path = Path.Combine(WorkspaceService.DataDirectory, "settings.json"); var before = await File.ReadAllBytesAsync(path);
        foreach (var json in new[] { "{", "{\"Unknown\":1}", "{\"Theme\":3}", "{\"FavoriteSettings\":42}" })
            await Assert.ThrowsAsync<JsonException>(() => WorkspaceService.ImportPreferences(Encoding.UTF8.GetBytes(json)));
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkspaceService.ImportPreferences("null"u8.ToArray()));
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkspaceService.ImportPreferences(new byte[1_000_001]));
        Assert.Throws<InvalidDataException>(() => WorkspaceService.ValidatePreferencesExportPath(path));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Equal("Light", WorkspaceService.Settings.Theme); Assert.Equal(["Theme"], Assert.IsType<string[]>(WorkspaceService.Settings.FavoriteSettings));
    }
}
