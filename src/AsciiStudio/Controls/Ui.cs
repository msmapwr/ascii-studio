using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using AsciiStudio.Services;

namespace AsciiStudio.Controls;

public static class Ui
{
    public static StackPanel Stack(double spacing = 12) => new() { Spacing = spacing };
    public static TextBlock Text(string text, double size = 14, bool secondary = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources[secondary ? "StudioSecondaryText" : "StudioText"] };
        void Apply(StudioSettings settings) { block.FontSize = size * settings.UiFontSize / 14; }
        Apply(WorkspaceService.Settings);
        block.Loaded += (_, _) => { Apply(WorkspaceService.Settings); WorkspaceService.SettingsChanged += Apply; };
        block.Unloaded += (_, _) => WorkspaceService.SettingsChanged -= Apply;
        return block;
    }
    public static StackPanel Heading(string title, string description)
    {
        var p = Stack(6); var t = Text(title, 30); t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        p.Children.Add(t); return p;
    }
    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Child = child,
        Padding = padding ?? new Thickness(20),
        CornerRadius = new CornerRadius(12),
        Style = (Style)Application.Current.Resources["StudioCard"],
        BorderThickness = new Thickness(1)
    };
    public static Button Button(string label, Action action, bool accent = false)
    {
        var b = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(b, Id("Button", label));
        if (accent) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        b.Click += (_, _) => action(); return b;
    }
    public static Button AsyncButton(string label, Func<Task> action, bool accent = false)
    {
        var b = Button(label, () => { }, accent);
        b.Click += async (_, _) => { b.IsEnabled = false; try { await App.Window.Guard(action); } finally { b.IsEnabled = true; } };
        return b;
    }
    public static ComboBox Choice(IEnumerable<string> values, int selected = 0)
    {
        var c = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var value in values) c.Items.Add(value);
        c.SelectedIndex = selected; return c;
    }
    public static FrameworkElement Field(string label, UIElement control)
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(control, Id("Field", label));
        if (control is NumberBox number)
        {
            number.Loaded += (_, _) =>
            {
                if (FindDescendant<TextBox>(number) is not { } input) return;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(input,
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(number) + "Input");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, label);
            };
        }
        var p = Stack(6);
        if (control is Slider slider)
        {
            var header = new Grid();
            header.Children.Add(Text(label, 12, true));
            var value = Text(slider.Value.ToString("0.##"), 12, true);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.Loaded += (_, _) => Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(value,
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(slider) + "Value");
            slider.ValueChanged += (_, _) => value.Text = slider.Value.ToString("0.##");
            header.Children.Add(value); p.Children.Add(header);
        }
        else p.Children.Add(Text(label, 12, true));
        p.Children.Add(control); AddHelp(p, label); return p;
    }
    public static FrameworkElement WithHelp(FrameworkElement control, string label)
    {
        var panel = Stack(6); panel.Children.Add(control); AddHelp(panel, label); return panel;
    }
    private static void AddHelp(StackPanel panel, string label)
    {
        var description = label switch
        {
            "列数" or "默认列数" or "字符网格宽度（列）" or "字符画列数（系统字体模式）" => "每行的字符数量。列数越多，细节越丰富，转换和显示也会更慢。",
            "行数" or "字符网格高度（行）" => "自动行数会保持比例；固定行数可能拉伸字符画。",
            "字符宽高比" or "字符宽高比 · 默认 0.5" => "一个字符的宽度除以行高，用来补偿字符通常不是正方形的问题。",
            "亮度" => "提高亮度会使更多区域使用较浅的字符。",
            "对比度" => "提高对比度会加强明暗差异。",
            "Gamma" => "调整中间亮度区域，1 表示保持原亮度关系。",
            "饱和度" => "控制色彩强度，0 接近灰度。",
            "色相" => "沿色环改变颜色。",
            "抖动" or "抖动算法" => "用相邻字符分散误差，使渐变更平滑。",
            "字符集" or "字符集 · 由浅到深" or "字符风格" => "从浅到深排列字符，决定画面使用的纹理。",
            "输出分辨率预设" => "这里设置字符网格，原图像素尺寸会按字体换算；默认保持原图比例。",
            "保持原图比例（自动计算行数）" or "按字体实际宽高补偿比例" => "推荐开启，让行数同时考虑图片比例和实际字符宽高。",
            "灰度" => "增加灰度会逐渐去除原图色彩。",
            "棕褐色" => "为图片加入复古暖色调。",
            "锐化" => "加强边界附近的细节，过高可能产生明显噪点。",
            "保留原图颜色" => "颜色可在图像预览和 PNG、HTML、SVG 等格式中保留。",
            "反转亮暗" => "交换浅色和深色字符，用于不同背景。",
            "边缘检测" => "突出物体轮廓，适合线条和标志。",
            "二值化" or "阈值" => "按阈值分成亮暗两组，适合黑白风格。",
            "边框" => "在文字结果外增加边框。",
            "替换空格" => "替换生成结果中的空格，可改变背景纹理。",
            "边框 / 分隔线样式" => "选择生成图案使用的字符样式。",
            "随机种子" => "相同参数和种子会得到相同图案。",
            "转换方式" => "FIGlet 生成预设艺术字；系统字体模式可以处理中文。",
            "中文字画风格" => "选择实心、细线、点阵或灰阶纹理，不改变输入文字。",
            "字体" or "系统字体" or "字符画字体" => "搜索已安装字体。字符画优先使用等宽字体，中文可选中文常用字体。",
            "字号 · 同时用于图片导出" => "改变字符画的基础字号和位图导出大小；底部缩放按钮仅影响预览。",
            "图片导出倍率" => "放大位图导出的像素尺寸，不改变字符数量。",
            "导出格式" => "TXT 保存纯文本；PNG 保存外观；HTML 和 SVG 支持颜色。",
            "注释语言" => "选择目标代码语言，再预览、复制或确认替换结果。",
            "注释形式" => "优先行注释最稳妥；块注释会检查结束符冲突。",
            "界面字体" => "仅调整应用界面，字符画字体仍在结果栏独立设置。",
            "界面字号" => "12–24，标题按同一比例调整；窄窗口可从顶部按钮展开输入。",
            "新手模式" => "在细致的设置下显示说明，关闭后保持简洁界面。",
            "调整参数后自动转换" => "开启后，图片参数改变会在短暂等待后合并转换。",
            "自动转换等待时间（毫秒）" => "等待可合并连续调整，减少重复处理。",
            "左（%）" or "上（%）" or "左侧 %" or "顶部 %" => "裁剪起点占原图尺寸的百分比。",
            "宽（%）" or "高（%）" or "宽度 %" or "高度 %" => "裁剪区域占原图尺寸的百分比，不能超出图像边界。",
            "算法" => "现代加密可以还原；摘要不能解密；编码和传统方法不提供现代安全保护。",
            "口令 / 参数" or "口令 / 密钥 / 教学参数" => "现代加密使用口令派生密钥，不会保存口令；传统方法使用各自的参数。",
            "RSA 公钥（PEM）" => "加密使用公钥，可以导入或在应用内生成。",
            "RSA 私钥（PEM，仅用于解密）" => "解密需要对应私钥。私钥不写入项目或偏好，请自行保管。",
            _ => null
        };
        if (description is null) return;
        var help = Text(description, 12, true); panel.Children.Add(help);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(help, Id("Help", label));
        void Apply(StudioSettings settings) => help.Visibility = settings.BeginnerMode ? Visibility.Visible : Visibility.Collapsed;
        Apply(WorkspaceService.Settings);
        // Attach the subscription to the parent so a collapsed explanation can still be enabled.
        panel.Loaded += (_, _) => { Apply(WorkspaceService.Settings); WorkspaceService.SettingsChanged -= Apply; WorkspaceService.SettingsChanged += Apply; };
        panel.Unloaded += (_, _) => WorkspaceService.SettingsChanged -= Apply;
    }
    public static void ApplyTypeface(DependencyObject root)
    {
        var settings = WorkspaceService.Settings;
        if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(root) is "ResultEditor" or "CommentPreview" or "CryptoOutput") return;
        if (root is Control control)
        {
            control.FontFamily = new FontFamily(settings.UiFontFamily); control.FontSize = settings.UiFontSize;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) ApplyTypeface(VisualTreeHelper.GetChild(root, i));
    }
    public static Slider Slider(double min, double max, double value, double step = 1) => new Slider()
    { Minimum = min, Maximum = max, Value = value, StepFrequency = step, IsThumbToolTipEnabled = true };
    private static string Id(string prefix, string label) => prefix + "_" + new string(label.Where(char.IsLetterOrDigit).ToArray());
    public static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
    public static Grid SettingsGrid(params FrameworkElement[] fields)
    {
        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 12 };
        foreach (var field in fields) grid.Children.Add(field);
        var previousColumns = 0;
        void Arrange(int columns)
        {
            if (columns == previousColumns) return;
            previousColumns = columns;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < fields.Length; i++)
            {
                if (i % columns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Grid.SetRow(fields[i], i / columns); Grid.SetColumn(fields[i], i % columns);
            }
        }
        Arrange(2);
        grid.SizeChanged += (_, _) => Arrange(grid.ActualWidth < 410 ? 1 : 2);
        return grid;
    }

    public static Flyout AdaptiveFlyout(FrameworkElement content, double preferredWidth = 420, FrameworkElement? anchor = null)
    {
        if (double.IsFinite(content.Width)) preferredWidth = content.Width;
        content.Width = double.NaN;
        var scroll = content as ScrollViewer ?? new ScrollViewer { Content = content, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (string.IsNullOrEmpty(Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(scroll)))
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(scroll, "AdaptiveSettingsScroll");
        var flyout = new Flyout { Content = scroll, ShowMode = Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowMode.Standard };
        // The content owns scrolling; disable the presenter's second scroller.
        var presenterStyle = new Style { TargetType = typeof(FlyoutPresenter) };
        presenterStyle.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        presenterStyle.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        flyout.FlyoutPresenterStyle = presenterStyle;
        XamlRoot? activeRoot = null;
        void Resize()
        {
            if (activeRoot is null) return;
            scroll.Width = Math.Max(1, Math.Min(preferredWidth, activeRoot.Size.Width - 64));
            scroll.MaxHeight = Math.Max(80, activeRoot.Size.Height - 160);
            if (activeRoot.Size.Width < 640 && anchor?.XamlRoot is not null)
            {
                var bottom = anchor.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, anchor.ActualHeight)).Y;
                scroll.MaxHeight = Math.Max(80, Math.Min(scroll.MaxHeight, activeRoot.Size.Height - bottom - 32));
            }
        }
        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
        flyout.Opened += (_, _) =>
        {
            ApplyTypeface(scroll);
            activeRoot = scroll.XamlRoot;
            if (activeRoot is not null) activeRoot.Changed += RootChanged;
            Resize();
        };
        flyout.Opening += (_, _) =>
        {
            activeRoot = (App.Window.Content as FrameworkElement)?.XamlRoot;
            Resize();
        };
        flyout.Closed += (_, _) =>
        {
            if (activeRoot is not null) activeRoot.Changed -= RootChanged;
            activeRoot = null;
        };
        return flyout;
    }

    public static Grid Workspace(UIElement input, FrameworkElement result)
    {
        var g = new Grid { ColumnSpacing = 20, RowSpacing = 12 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(g, "ResponsiveWorkspace");
        g.RowDefinitions.Add(new() { Height = GridLength.Auto });
        g.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var scroller = new ScrollViewer { Content = input, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(scroller, "ParameterScroll");
        var inputFlyout = AdaptiveFlyout(scroller, 320);
        // A flyout and the inline layout must never own the same element.
        inputFlyout.Content = null;
        var inputButton = new Button { Content = "输入与常用设置", Flyout = inputFlyout, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(inputButton, "WorkspaceInputButton");
        g.Children.Add(inputButton); Grid.SetRow(scroller, 1); g.Children.Add(scroller);
        Grid.SetColumn(result, 1); Grid.SetRow(result, 1); g.Children.Add(result);
        var narrow = false;
        void Reflow()
        {
            if (g.ActualWidth <= 0) return;
            var next = g.ActualWidth < 760 || g.ActualHeight < 420;
            if (next == narrow) return;
            narrow = next;
            inputFlyout.Hide();
            if (narrow)
            {
                g.Children.Remove(scroller); inputFlyout.Content = scroller;
                g.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                g.ColumnDefinitions[1].Width = new GridLength(0);
                Grid.SetColumn(result, 0); inputButton.Visibility = Visibility.Visible;
            }
            else
            {
                inputFlyout.Content = null; scroller.Width = double.NaN; scroller.MaxHeight = double.PositiveInfinity;
                g.Children.Add(scroller);
                g.ColumnDefinitions[0].Width = new GridLength(280);
                g.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(result, 1); inputButton.Visibility = Visibility.Collapsed;
            }
        }
        g.SizeChanged += (_, _) => Reflow();
        return g;
    }

    public static Grid ResponsiveCards(params FrameworkElement[] cards)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        foreach (var card in cards) grid.Children.Add(card);
        var previous = 0;
        void Reflow()
        {
            var count = grid.ActualWidth >= 840 ? 3 : grid.ActualWidth >= 560 ? 2 : 1;
            if (count == previous) return;
            previous = count; grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var c = 0; c < count; c++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < cards.Length; i++)
            {
                if (i % count == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Grid.SetRow(cards[i], i / count); Grid.SetColumn(cards[i], i % count);
            }
        }
        grid.SizeChanged += (_, _) => Reflow(); Reflow(); return grid;
    }
    public static Grid Page(UIElement heading, FrameworkElement content)
    {
        var grid = new Grid { RowSpacing = 24 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(heading); Grid.SetRow(content, 1); grid.Children.Add(content);
        grid.Loaded += (_, _) => ApplyTypeface(grid);
        grid.SizeChanged += (_, _) =>
        {
            var shortWindow = grid.ActualHeight < 420;
            grid.RowSpacing = shortWindow ? 12 : 24;
            if (heading is StackPanel title && title.Children.Count >= 1)
            {
                if (title.Children[0] is TextBlock text) text.FontSize = (grid.ActualWidth < 640 ? 24 : 30) * WorkspaceService.Settings.UiFontSize / 14;
                if (title.Children.Count >= 2) title.Children[1].Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
            }
        };
        return grid;
    }
}
