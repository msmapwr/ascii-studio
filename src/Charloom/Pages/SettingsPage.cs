using Charloom.Controls;
using Charloom.Services;
using Charloom.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Storage.Pickers;

namespace Charloom.Pages;

public sealed class SettingsPage : Grid
{
    private bool refreshing;
    private readonly List<Action<StudioSettings>> refresh = [];
    private readonly List<(SettingEntry Entry, FrameworkElement Row, ToggleButton Favorite)> settingRows = [];
    private readonly List<(StackPanel Group, Border Card)> groups = [];
    private readonly TextBox search = new() { PlaceholderText = "搜索设置（中文 / English / 设置键）", MaxLength = 256 };
    private readonly CheckBox favoritesOnly = new() { Content = "仅显示收藏", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock searchStatus = Ui.Text("", 12, true);
    private readonly InfoBar feedback = new() { IsOpen = false, IsClosable = true };

    public SettingsPage()
    {
        var content = Ui.Stack(20);
        var common = Group(content, "常用与外观");
        Choice(common, "主题", "SettingsTheme", ["深色", "浅色", "跟随系统"], s => s.Theme switch { "Light" => 1, "System" => 2, _ => 0 }, (s, i) => s with { Theme = i switch { 1 => "Light", 2 => "System", _ => "Dark" } });
        Toggle(common, "动画（遵循 Windows 设置）", "SettingsAnimations", s => s.Animations, (s, v) => s with { Animations = v });
        Toggle(common, "新手模式", "SettingsBeginner", s => s.BeginnerMode, (s, v) => s with { BeginnerMode = v });
        var uiFont = new FontPicker("SettingsUiFont");
        common.Children.Add(Field("界面字体", "SettingsUiFontContainer", uiFont));
        refresh.Add(s => uiFont.Select(s.UiFontFamily));
        uiFont.Changed += async family => { if (!refreshing) await Save(s => s with { UiFontFamily = family }); };
        Number(common, "界面字号", "SettingsUiFontSize", 12, 24, s => s.UiFontSize, (s, v) => s with { UiFontSize = v });
        Toggle(common, "紧凑布局", "SettingsCompact", s => s.CompactLayout, (s, v) => s with { CompactLayout = v });
        Toggle(common, "记住窗口尺寸与位置", "SettingsRememberWindow", s => s.RememberWindow, (s, v) => s with { RememberWindow = v });
        var editing = Group(content, "编辑");
        Number(editing, "默认字号", "SettingsFontSize", 8, 30, s => s.PreviewFontSize, (s, v) => s with { PreviewFontSize = v });
        Number(editing, "默认预览缩放", "SettingsPreviewZoom", .25, 4, s => s.PreviewZoom, (s, v) => s with { PreviewZoom = v });
        Toggle(editing, "结果自动换行", "SettingsWordWrap", s => s.WordWrap, (s, v) => s with { WordWrap = v });
        Toggle(editing, "显示结果统计", "SettingsShowStats", s => s.ShowStats, (s, v) => s with { ShowStats = v });
        var conversion = Group(content, "图片转换");
        Toggle(conversion, "调整参数后自动转换", "SettingsAutoConvert", s => s.AutoConvert, (s, v) => s with { AutoConvert = v });
        Number(conversion, "自动转换等待时间（毫秒）", "SettingsDelay", 0, 1000, s => s.ConversionDelay, (s, v) => s with { ConversionDelay = (int)v });
        Number(conversion, "默认列数", "SettingsColumns", 8, ImageResourceLimits.GridSide, s => s.DefaultColumns, (s, v) => s with { DefaultColumns = (int)v });
        var export = Group(content, "导出");
        string[] formats = ["TXT", "PNG", "JPEG", "GIF", "HTML", "SVG", "ANSI", "JSON", "Markdown"];
        Choice(export, "默认格式", "SettingsExportFormat", formats, s => Array.IndexOf(formats, s.DefaultExportFormat), (s, i) => s with { DefaultExportFormat = formats[i] });
        Choice(export, "默认图片倍率", "SettingsExportScale", ["1×", "2×", "3×", "4×"], s => s.ExportScale - 1, (s, i) => s with { ExportScale = i + 1 });
        var prefix = new TextBox { MaxLength = 32 };
        export.Children.Add(Field("文件名前缀", "SettingsFilePrefix", prefix));
        refresh.Add(s => prefix.Text = s.FilePrefix);
        prefix.LostFocus += async (_, _) => { if (!refreshing) await Save(s => s with { FilePrefix = prefix.Text }); };
        var actions = Ui.Stack();
        var import = Ui.AsyncButton("导入偏好…", Import);
        var exportButton = Ui.AsyncButton("导出偏好…", Export);
        AutomationProperties.SetAutomationId(import, "SettingsImport");
        AutomationProperties.SetAutomationId(exportButton, "SettingsExport");
        actions.Children.Add(Ui.SettingsGrid(import, exportButton));
        var reset = Ui.AsyncButton("恢复默认设置", Reset);
        AutomationProperties.SetAutomationId(reset, "SettingsReset"); actions.Children.Add(reset);
        actions.Children.Add(Ui.AsyncButton("清空最近项目记录", ClearRecent));
        content.Children.Add(actions);
        AutomationProperties.SetAutomationId(search, "SettingsSearch"); AutomationProperties.SetName(search, "搜索设置");
        AutomationProperties.SetAutomationId(favoritesOnly, "SettingsFavoritesOnly");
        AutomationProperties.SetAutomationId(searchStatus, "SettingsSearchStatus");
        AutomationProperties.SetAutomationId(feedback, "SettingsFeedback");
        var filters = new Grid { ColumnSpacing = 12 };
        filters.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        filters.Children.Add(search); Grid.SetColumn(favoritesOnly, 1); filters.Children.Add(favoritesOnly);
        var header = Ui.Stack(8); header.Children.Add(filters); header.Children.Add(searchStatus); header.Children.Add(feedback);
        var body = new Grid { RowSpacing = 12 };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(header);
        var scroller = new ScrollViewer { Content = content, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroller, 1); body.Children.Add(scroller);
        Children.Add(Ui.Page(Ui.Heading("设置", ""), body));
        search.TextChanged += (_, _) => Filter();
        favoritesOnly.Checked += (_, _) => Filter(); favoritesOnly.Unchecked += (_, _) => Filter();
        Apply(WorkspaceService.Settings);
        Loaded += (_, _) => WorkspaceService.SettingsChanged += Apply;
        Unloaded += (_, _) => WorkspaceService.SettingsChanged -= Apply;
    }

    private StackPanel Group(StackPanel content, string title)
    {
        var group = Ui.Stack(16); group.Children.Add(Ui.Text(title, 20)); var card = Ui.Card(group);
        groups.Add((group, card)); content.Children.Add(card); return group;
    }
    private FrameworkElement Field(string label, string id, FrameworkElement element)
    {
        var field = Ui.Field(label, element); AutomationProperties.SetAutomationId(element, id); return SettingRow(id, field);
    }
    private FrameworkElement SettingRow(string id, FrameworkElement field)
    {
        var entry = SettingsCatalog.Entries.Single(entry => entry.AutomationId == id);
        var favorite = new ToggleButton { Content = "☆", Width = 36, MinWidth = 36, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetAutomationId(favorite, "SettingsFavorite_" + entry.Key);
        AutomationProperties.SetName(favorite, "收藏设置：" + entry.Name);
        favorite.Checked += async (_, _) => { if (!refreshing) await Save(s => SettingsCatalog.SetFavorite(s, entry.Key, true)); };
        favorite.Unchecked += async (_, _) => { if (!refreshing) await Save(s => SettingsCatalog.SetFavorite(s, entry.Key, false)); };
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(field); Grid.SetColumn(favorite, 1); row.Children.Add(favorite);
        settingRows.Add((entry, row, favorite)); return row;
    }
    private void Choice(StackPanel group, string label, string id, string[] items, Func<StudioSettings, int> read, Func<StudioSettings, int, StudioSettings> write)
    {
        var control = Ui.Choice(items); group.Children.Add(Field(label, id, control));
        refresh.Add(s => control.SelectedIndex = read(s));
        control.SelectionChanged += async (_, _) => { if (!refreshing && control.SelectedIndex >= 0) { var value = control.SelectedIndex; await Save(s => write(s, value)); } };
    }
    private void Toggle(StackPanel group, string label, string id, Func<StudioSettings, bool> read, Func<StudioSettings, bool, StudioSettings> write)
    {
        var control = new ToggleSwitch { Header = label }; AutomationProperties.SetAutomationId(control, id); group.Children.Add(SettingRow(id, Ui.WithHelp(control, label)));
        refresh.Add(s => control.IsOn = read(s));
        control.Toggled += async (_, _) => { if (!refreshing) { var value = control.IsOn; await Save(s => write(s, value)); } };
    }
    private void Number(StackPanel group, string label, string id, double min, double max, Func<StudioSettings, double> read, Func<StudioSettings, double, StudioSettings> write)
    {
        var control = new NumberBox { Minimum = min, Maximum = max, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        group.Children.Add(Field(label, id, control)); refresh.Add(s => control.Value = read(s));
        control.ValueChanged += async (_, _) => { if (!refreshing && double.IsFinite(control.Value)) { var value = control.Value; await Save(s => write(s, value)); } };
    }
    private Task Save(Func<StudioSettings, StudioSettings> update) => App.Window.Guard(() => WorkspaceService.UpdateSettings(update));
    private void Apply(StudioSettings settings)
    {
        refreshing = true;
        try
        {
            foreach (var action in refresh) action(settings);
            foreach (var row in settingRows)
            {
                var selected = settings.FavoriteSettings?.Contains(row.Entry.Key) == true;
                row.Favorite.IsChecked = selected; row.Favorite.Content = selected ? "★" : "☆";
                ToolTipService.SetToolTip(row.Favorite, selected ? "取消收藏：" + row.Entry.Name : "收藏设置：" + row.Entry.Name);
            }
            Filter();
        }
        finally { refreshing = false; }
    }
    private void Filter()
    {
        var keys = SettingsCatalog.Search(search.Text, WorkspaceService.Settings, favoritesOnly.IsChecked == true).Select(entry => entry.Key).ToHashSet();
        foreach (var row in settingRows) row.Row.Visibility = keys.Contains(row.Entry.Key) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var group in groups) group.Card.Visibility = group.Group.Children.Skip(1).Any(row => row.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        searchStatus.Text = keys.Count == 0 ? "没有匹配的设置。请清空搜索或关闭“仅显示收藏”。" : $"显示 {keys.Count} / {settingRows.Count} 项设置";
    }
    private async Task Import()
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json"); App.Window.InitializePicker(picker);
        var file = await picker.PickSingleFileAsync(); if (file is null) return;
        await WorkspaceService.ImportPreferences(await BoundedFile.ReadAsync(file.Path, 1_000_000));
        feedback.Title = "已导入偏好"; feedback.Message = "界面、转换、导出和设置收藏已更新，最近项目记录已保留。";
        feedback.Severity = InfoBarSeverity.Success; feedback.IsOpen = true;
    }
    private async Task Export()
    {
        var picker = new FileSavePicker { SuggestedFileName = "charloom-preferences" };
        picker.FileTypeChoices.Add("Charloom 偏好", [".json"]); App.Window.InitializePicker(picker);
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        WorkspaceService.ValidatePreferencesExportPath(file.Path);
        await WorkspaceService.AtomicWrite(file.Path, WorkspaceService.ExportPreferences());
        feedback.Title = "已导出偏好"; feedback.Message = "包含设置收藏，不包含最近项目路径。";
        feedback.Severity = InfoBarSeverity.Success; feedback.IsOpen = true;
    }
    private async Task Reset()
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "恢复默认设置？", Content = "界面、转换和导出偏好将恢复默认值，设置收藏将清空，最近项目记录保留。", PrimaryButtonText = "恢复默认", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await WorkspaceService.ResetPreferences();
    }
    private async Task ClearRecent()
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "清空最近项目记录？", PrimaryButtonText = "清空记录", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await WorkspaceService.UpdateSettings(s => s with { RecentFiles = [] });
    }
}
