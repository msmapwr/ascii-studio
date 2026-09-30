using AsciiStudio.Controls;
using AsciiStudio.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace AsciiStudio.Pages;

public sealed class SettingsPage : Grid
{
    private bool refreshing;
    private readonly List<Action<StudioSettings>> refresh = [];

    public SettingsPage()
    {
        var content = Ui.Stack(20);
        var common = Group(content, "常用与外观");
        Choice(common, "主题", "SettingsTheme", ["深色", "浅色", "跟随系统"], s => s.Theme switch { "Light" => 1, "System" => 2, _ => 0 }, (s, i) => s with { Theme = i switch { 1 => "Light", 2 => "System", _ => "Dark" } });
        Toggle(common, "动画（遵循 Windows 设置）", "SettingsAnimations", s => s.Animations, (s, v) => s with { Animations = v });
        Toggle(common, "紧凑布局", "SettingsCompact", s => s.CompactLayout, (s, v) => s with { CompactLayout = v });
        Toggle(common, "记住窗口尺寸与位置", "SettingsRememberWindow", s => s.RememberWindow, (s, v) => s with { RememberWindow = v });
        var editing = Group(content, "编辑");
        Number(editing, "默认字号", "SettingsFontSize", 8, 30, s => s.PreviewFontSize, (s, v) => s with { PreviewFontSize = v });
        Toggle(editing, "结果自动换行", "SettingsWordWrap", s => s.WordWrap, (s, v) => s with { WordWrap = v });
        Toggle(editing, "显示结果统计", "SettingsShowStats", s => s.ShowStats, (s, v) => s with { ShowStats = v });
        var conversion = Group(content, "图片转换");
        Toggle(conversion, "调整参数后自动转换", "SettingsAutoConvert", s => s.AutoConvert, (s, v) => s with { AutoConvert = v });
        Number(conversion, "自动转换等待时间（毫秒）", "SettingsDelay", 0, 1000, s => s.ConversionDelay, (s, v) => s with { ConversionDelay = (int)v });
        Number(conversion, "默认列数", "SettingsColumns", 8, 2000, s => s.DefaultColumns, (s, v) => s with { DefaultColumns = (int)v });
        var export = Group(content, "导出");
        string[] formats = ["TXT", "PNG", "JPEG", "GIF", "HTML", "SVG", "ANSI", "JSON", "Markdown"];
        Choice(export, "默认格式", "SettingsExportFormat", formats, s => Array.IndexOf(formats, s.DefaultExportFormat), (s, i) => s with { DefaultExportFormat = formats[i] });
        Choice(export, "默认图片倍率", "SettingsExportScale", ["1×", "2×", "3×", "4×"], s => s.ExportScale - 1, (s, i) => s with { ExportScale = i + 1 });
        var prefix = new TextBox { MaxLength = 32 };
        export.Children.Add(Field("文件名前缀", "SettingsFilePrefix", prefix));
        refresh.Add(s => prefix.Text = s.FilePrefix);
        prefix.LostFocus += async (_, _) => { if (!refreshing) await Save(s => s with { FilePrefix = prefix.Text }); };
        var reset = Ui.AsyncButton("恢复默认设置", Reset);
        AutomationProperties.SetAutomationId(reset, "SettingsReset"); content.Children.Add(reset);
        content.Children.Add(Ui.AsyncButton("清空最近项目记录", ClearRecent));
        Children.Add(Ui.Page(Ui.Heading("设置", ""), new ScrollViewer { Content = content, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }));
        Apply(WorkspaceService.Settings);
        Loaded += (_, _) => WorkspaceService.SettingsChanged += Apply;
        Unloaded += (_, _) => WorkspaceService.SettingsChanged -= Apply;
    }

    private static StackPanel Group(StackPanel content, string title)
    {
        var group = Ui.Stack(16); group.Children.Add(Ui.Text(title, 20)); content.Children.Add(Ui.Card(group)); return group;
    }
    private static FrameworkElement Field(string label, string id, FrameworkElement element)
    {
        var field = Ui.Field(label, element); AutomationProperties.SetAutomationId(element, id); return field;
    }
    private void Choice(StackPanel group, string label, string id, string[] items, Func<StudioSettings, int> read, Func<StudioSettings, int, StudioSettings> write)
    {
        var control = Ui.Choice(items); group.Children.Add(Field(label, id, control));
        refresh.Add(s => control.SelectedIndex = read(s));
        control.SelectionChanged += async (_, _) => { if (!refreshing && control.SelectedIndex >= 0) { var value = control.SelectedIndex; await Save(s => write(s, value)); } };
    }
    private void Toggle(StackPanel group, string label, string id, Func<StudioSettings, bool> read, Func<StudioSettings, bool, StudioSettings> write)
    {
        var control = new ToggleSwitch { Header = label }; AutomationProperties.SetAutomationId(control, id); group.Children.Add(control);
        refresh.Add(s => control.IsOn = read(s));
        control.Toggled += async (_, _) => { if (!refreshing) { var value = control.IsOn; await Save(s => write(s, value)); } };
    }
    private void Number(StackPanel group, string label, string id, int min, int max, Func<StudioSettings, double> read, Func<StudioSettings, double, StudioSettings> write)
    {
        var control = new NumberBox { Minimum = min, Maximum = max, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        group.Children.Add(Field(label, id, control)); refresh.Add(s => control.Value = read(s));
        control.ValueChanged += async (_, _) => { if (!refreshing && double.IsFinite(control.Value)) { var value = control.Value; await Save(s => write(s, value)); } };
    }
    private Task Save(Func<StudioSettings, StudioSettings> update) => App.Window.Guard(() => WorkspaceService.UpdateSettings(update));
    private void Apply(StudioSettings settings) { refreshing = true; try { foreach (var action in refresh) action(settings); } finally { refreshing = false; } }
    private async Task Reset()
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "恢复默认设置？", Content = "界面、转换和导出偏好将恢复默认值。", PrimaryButtonText = "恢复默认", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await WorkspaceService.UpdateSettings(s => new StudioSettings(RecentFiles: s.RecentFiles));
    }
    private async Task ClearRecent()
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "清空最近项目记录？", PrimaryButtonText = "清空记录", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await WorkspaceService.UpdateSettings(s => s with { RecentFiles = [] });
    }
}
