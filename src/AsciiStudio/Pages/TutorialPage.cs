using AsciiStudio.Controls;
using AsciiStudio.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace AsciiStudio.Pages;

public sealed class TutorialPage : Grid
{
    public TutorialPage()
    {
        var content = Ui.Stack(16);
        var beginner = new ToggleSwitch { Header = "新手模式", IsOn = WorkspaceService.Settings.BeginnerMode };
        AutomationProperties.SetAutomationId(beginner, "TutorialBeginner");
        beginner.Toggled += async (_, _) => await App.Window.Guard(() => WorkspaceService.UpdateSettings(s => s with { BeginnerMode = beginner.IsOn }));
        void Apply(StudioSettings settings) { if (beginner.IsOn != settings.BeginnerMode) beginner.IsOn = settings.BeginnerMode; }
        Loaded += (_, _) => { Apply(WorkspaceService.Settings); WorkspaceService.SettingsChanged += Apply; };
        Unloaded += (_, _) => WorkspaceService.SettingsChanged -= Apply;
        content.Children.Add(Ui.WithHelp(beginner, "新手模式"));
        Add("1. 图片变成字符画", "打开图片 → 选择列数 → 转换。默认按字体宽高补偿比例；在结果栏上方调整字符、颜色和裁剪。自动转换会合并连续调整。", "image");
        Add("2. 文字变成艺术字", "输入文字并选择转换方式。英文可使用 FIGlet，中文选择系统字体模式。输入字体负责字形，结果字体负责字符画的排列。", "text");
        Add("3. 查看和编辑", "结果可以直接编辑。− / ＋ 和 Ctrl+滚轮调整预览，100% 恢复；Shift+滚轮横向移动。预览缩放不改变导出尺寸。窄窗口从顶部展开输入。", "text");
        Add("4. 保存、导出与套注释", "保存项目可以继续调整原图和参数。导出选择 TXT、PNG、HTML 等格式；位图倍率独立设置。注释工具先预览再复制或替换，当前会话可恢复原文。", "library");
        Add("5. 加密、摘要和编码", "使用当前字符画或粘贴文本。现代加密需要口令，可还原；摘要不可还原；编码和传统方法用于格式转换或教学。RSA 解密需要对应私钥，请自行妥善保管。", "crypto");
        Add("6. 图案与文本处理", "生成器提供边框、分隔线和图案；文本工具可以反转、整理和处理已有字符画。", "generators");
        Add("7. 查看 ANSI 作品", "打开文件或载入彩色示例。乱码时在 ANSI 设置切换 UTF-8/CP437；经典作品通常为 80 列。保存项目保留原文，重新查看会解析原文并替换结果。", "ansi");
        content.Children.Add(Ui.Text("在设置中调整界面字体和字号；字符画字体仍在结果栏的“显示”中调整。新手模式可随时关闭。", 14));
        Children.Add(Ui.Page(Ui.Heading("使用教程", ""), new ScrollViewer { Content = content }));
        void Add(string title, string description, string target)
        {
            var card = Ui.Stack(8); card.Children.Add(Ui.Text(title, 20)); card.Children.Add(Ui.Text(description));
            card.Children.Add(Ui.Button("打开 · " + title[3..], () => App.Window.Navigate(target))); content.Children.Add(Ui.Card(card));
        }
    }
}
