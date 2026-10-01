using AsciiStudio.Controls;
using AsciiStudio.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using System.Runtime.InteropServices;

namespace AsciiStudio;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, UIElement> pages = [];
    private WindowPlacement? normalPlacement;
    private bool placementMaximized;
    private int themeVersion;
    private string? appliedUiFont;
    private double appliedUiSize;
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    public MainWindow()
    {
        InitializeComponent(); Title = "AsciiStudio — 字符创作工作室";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AsciiStudio.ico"));
        var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Min((int)(1280 * scale), work.Width - 40), Math.Min((int)(820 * scale), work.Height - 40)));
        AppWindow.Move(new Windows.Graphics.PointInt32(work.X + (work.Width - AppWindow.Size.Width) / 2, work.Y + (work.Height - AppWindow.Size.Height) / 2));
        RestorePlacement();
        UpdateMinimumSize(GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d);
        CapturePlacement();
        AppWindow.Changed += (_, _) => CapturePlacement();
        Closed += (_, _) =>
        {
            WorkspaceService.SettingsChanged -= OnSettingsChanged;
            if (normalPlacement is null || !WorkspaceService.Settings.RememberWindow) return;
            try
            {
                WindowPlacementService.Save(normalPlacement with { Maximized = placementMaximized });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(ex); }
        };
        Root.SizeChanged += (_, _) =>
        {
            ApplyDensity();
        };
        Root.Loaded += (_, _) => Root.XamlRoot.Changed += (_, _) => UpdateMinimumSize(Root.XamlRoot.RasterizationScale);
        Root.RequestedTheme = WorkspaceService.Settings.Theme switch { "Light" => ElementTheme.Light, "System" => ElementTheme.Default, _ => ElementTheme.Dark };
        if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported()) SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        Navigation.SelectedItem = Navigation.MenuItems[0];
        WorkspaceService.SettingsChanged += OnSettingsChanged;
        Root.Loaded += (_, _) => ApplyUiFont();
    }

    private void CapturePlacement()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Maximized }) placementMaximized = true;
        if (AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Restored }) return;
        placementMaximized = false;
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        normalPlacement = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width / scale, AppWindow.Size.Height / scale, false);
    }

    private void RestorePlacement()
    {
        if (!WorkspaceService.Settings.RememberWindow) return;
        var saved = WindowPlacementService.Load();
        if (saved is null) return;
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(saved.X, saved.Y), Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Move(new Windows.Graphics.PointInt32(Math.Clamp(saved.X, area.X, area.X + Math.Max(0, area.Width - 100)), Math.Clamp(saved.Y, area.Y, area.Y + Math.Max(0, area.Height - 100))));
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        var width = Math.Min((int)Math.Round(saved.Width * scale), area.Width);
        var height = Math.Min((int)Math.Round(saved.Height * scale), area.Height);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        AppWindow.Move(new Windows.Graphics.PointInt32(Math.Clamp(saved.X, area.X, area.X + Math.Max(0, area.Width - width)), Math.Clamp(saved.Y, area.Y, area.Y + Math.Max(0, area.Height - height))));
        CapturePlacement();
        if (saved.Maximized && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Maximize();
    }

    private void ApplyDensity()
    {
        var compact = Root.ActualWidth < 640;
        var dense = WorkspaceService.Settings.CompactLayout;
        Root.RowDefinitions[0].Height = new GridLength(dense ? 48 : 64);
        PageContainer.Padding = compact ? new Thickness(12, 48, 12, 12) : dense ? new Thickness(16, 8, 16, 12) : new Thickness(24, 12, 24, 20);
    }

    private async void OnSettingsChanged(StudioSettings settings)
    {
        ApplyDensity();
        ApplyUiFont();
        var theme = settings.Theme switch { "Light" => ElementTheme.Light, "System" => ElementTheme.Default, _ => ElementTheme.Dark };
        await Guard(() => ChangeTheme(theme));
    }

    private async Task ChangeTheme(ElementTheme theme)
    {
        var version = ++themeVersion;
        if (Root.RequestedTheme == theme) { ThemeSnapshot.Visibility = Visibility.Collapsed; ThemeSnapshot.Source = null; return; }
        ThemeSnapshot.Visibility = Visibility.Collapsed;
        if (!MotionService.Enabled || Root.XamlRoot is null) { Root.RequestedTheme = theme; ThemeSnapshot.Source = null; return; }
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        try
        {
            var scale = Root.XamlRoot.RasterizationScale;
            await bitmap.RenderAsync(Root, (int)Math.Ceiling(Root.ActualWidth * scale), (int)Math.Ceiling(Root.ActualHeight * scale));
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            if (version == themeVersion) Root.RequestedTheme = theme;
            System.Diagnostics.Debug.WriteLine(ex); return;
        }
        if (version != themeVersion) return;
        Root.RequestedTheme = theme;
        ThemeSnapshot.Source = bitmap; ThemeSnapshot.Visibility = Visibility.Visible;
        try { await MotionService.Fade(ThemeSnapshot, 1, 0, 200); }
        finally { if (version == themeVersion) { ThemeSnapshot.Visibility = Visibility.Collapsed; ThemeSnapshot.Source = null; } }
    }

    private void ApplyUiFont()
    {
        var settings = WorkspaceService.Settings;
        if (appliedUiFont == settings.UiFontFamily && appliedUiSize == settings.UiFontSize) return;
        appliedUiFont = settings.UiFontFamily; appliedUiSize = settings.UiFontSize;
        var family = new Microsoft.UI.Xaml.Media.FontFamily(settings.UiFontFamily);
        Navigation.FontFamily = family; PageHost.FontFamily = family; BrandTitle.FontFamily = family;
        Navigation.FontSize = PageHost.FontSize = settings.UiFontSize; BrandTitle.FontSize = 19 * settings.UiFontSize / 14;
        Navigation.OpenPaneLength = Math.Clamp(210 * settings.UiFontSize / 14, 210, 360);
        Ui.ApplyTypeface(Root);
    }

    private void UpdateMinimumSize(double scale)
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)Math.Ceiling(480 * scale);
            presenter.PreferredMinimumHeight = (int)Math.Ceiling(480 * scale);
        }
    }

    public void InitializePicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
    public async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Message(ex.Message, true); await Log(ex); }
    }
    public void Message(string text, bool error = false)
    { Notice.Message = text; Notice.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Success; Notice.IsOpen = true; }
    private static async Task Log(Exception ex)
    {
        try { Directory.CreateDirectory(WorkspaceService.DataDirectory); await File.AppendAllTextAsync(Path.Combine(WorkspaceService.DataDirectory, "errors.log"), $"{DateTimeOffset.Now:O} {ex}\n"); }
        catch (IOException) { }
    }
    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var key = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "home";
        // Leave UI Automation's synchronous selection callback before building
        // and attaching the next page; WinUI disallows reentrant tree changes.
        DispatcherQueue.TryEnqueue(() => ShowPage(key));
    }
    private void ShowPage(string key)
    {
        try
        {
            Notice.IsOpen = false;
            if (key is "library" or "settings") pages.Remove(key);
            if (!pages.TryGetValue(key, out var page))
            {
                page = key switch
                {
                    "image" => new Pages.ImagePage(),
                    "text" => new Pages.TextPage(),
                    "ansi" => new Pages.AnsiPage(),
                    "generators" => new Pages.GeneratorPage(),
                    "tools" => new Pages.ToolsPage(),
                    "crypto" => new Pages.CryptoPage(),
                    "tutorial" => new Pages.TutorialPage(),
                    "library" => LibraryPage(),
                    "settings" => SettingsPage(),
                    _ => HomePage()
                };
                pages[key] = page;
            }
            PageHost.Content = page;
            _ = Guard(() => MotionService.Fade(PageHost, 0, 1));
        }
        catch (Exception ex) { Message(ex.Message, true); _ = Log(ex); }
    }
    public void Navigate(string key)
    { foreach (var item in Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>()) if (item.Tag?.ToString() == key) Navigation.SelectedItem = item; }

    private UIElement HomePage()
    {
        var content = Ui.Stack(24);
        var welcome = Ui.Stack(12);
        welcome.Children.Add(Ui.Text("从一张图片，一句话开始。", 25));
        var letters = new[] {
            new[] { "   ___   ", "  / _ \\  ", " / ___ \\ ", "/_/   \\_\\" },
            new[] { " ____ ", "/ ___|", "\\___ \\", "|____/" },
            new[] { "  ____ ", " / ___|", "| |    ", " \\____|" },
            new[] { " ___ ", "|_ _|", " | | ", "|___|" },
            new[] { " ___ ", "|_ _|", " | | ", "|___|" } };
        var cat = new[] { " /\\_/\\ ", "( o.o )", " > ^ < ", "" };
        var art = new TextBlock { Text = string.Join('\n', Enumerable.Range(0, 4).Select(row => cat[row].PadRight(12) + string.Join(' ', letters.Select(letter => letter[row])))), FontSize = 17, LineHeight = 22, TextWrapping = TextWrapping.NoWrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(art, "HomeWelcomeArt");
        art.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"); welcome.Children.Add(art);
        welcome.SizeChanged += (_, _) => art.Visibility = welcome.ActualWidth < 560 ? Visibility.Collapsed : Visibility.Visible;
        content.Children.Add(Ui.Card(welcome, new Thickness(30)));
        var cards = new List<FrameworkElement>();
        var labels = new[] { ("图片转换", "照片、插画、Logo。用密度和颜色保留细节。", "image"), ("文字转换", "FIGlet 艺术字，或支持中文的字体转换。", "text"), ("生成器", "边框、分隔线、迷宫、夜空与图案。", "generators") };
        for (var i = 0; i < labels.Length; i++)
        {
            var item = labels[i]; var p = Ui.Stack(); p.Children.Add(Ui.Text(item.Item1, 20)); p.Children.Add(Ui.Button("开始创作 →", () => Navigate(item.Item3)));
            var card = Ui.Card(p);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(card, "HomeCard_" + item.Item3);
            cards.Add(card);
        }
        content.Children.Add(Ui.ResponsiveCards(cards.ToArray()));
        var open = Ui.Stack(); open.Children.Add(Ui.Text("继续上次的创作", 19));
        open.Children.Add(Ui.AsyncButton("打开 .asciiproj 项目", PickProject));
        if (File.Exists(Path.Combine(WorkspaceService.DataDirectory, "recovery.asciiproj"))) open.Children.Add(Ui.AsyncButton("恢复最近一次结果", () => OpenProject(Path.Combine(WorkspaceService.DataDirectory, "recovery.asciiproj"))));
        content.Children.Add(Ui.Card(open));
        return Ui.Page(Ui.Heading("开始创作", "ASCII / UNICODE · 原生 Windows 工作室"), new ScrollViewer { Content = content });
    }

    private async Task PickProject()
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".asciiproj"); InitializePicker(picker);
        var file = await picker.PickSingleFileAsync(); if (file is not null) await OpenProject(file.Path);
    }
    public async Task OpenProject(string path)
    {
        var project = await WorkspaceService.OpenProject(path);
        if (project.Mode == "image")
        {
            var page = new Pages.ImagePage(); await page.LoadProject(project); pages["image"] = page; Navigate("image"); PageHost.Content = page;
        }
        else if (project.Mode == "text")
        {
            var page = new Pages.TextPage(); await page.LoadProject(project); pages["text"] = page; Navigate("text"); PageHost.Content = page;
        }
        else if (project.Mode == "generator")
        {
            var page = new Pages.GeneratorPage(); await page.LoadProject(project); pages["generators"] = page; Navigate("generators"); PageHost.Content = page;
        }
        else if (project.Mode == "ansi")
        {
            var page = new Pages.AnsiPage(); await page.LoadProject(project); pages["ansi"] = page; Navigate("ansi"); PageHost.Content = page;
        }
        else
        {
            var output = new ResultPane(); await output.SetDocument(project.Document); PageHost.Content = Ui.Page(Ui.Heading(project.Document.Title, "项目中的字符画快照"), output);
        }
        Message("项目已打开。");
    }
    private UIElement LibraryPage()
    {
        var p = Ui.Stack(12); p.Children.Add(Ui.AsyncButton("打开项目", PickProject, true));
        foreach (var path in WorkspaceService.Settings.RecentFiles ?? [])
        {
            var file = path; var row = Ui.Stack(6); row.Children.Add(Ui.Text(Path.GetFileNameWithoutExtension(file), 17)); row.Children.Add(Ui.Text(file, 12, true));
            var open = Ui.AsyncButton("打开", () => OpenProject(file));
            var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(file.ToUpperInvariant())))[..12];
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(open, "RecentProject_" + identity);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, "打开项目：" + Path.GetFileNameWithoutExtension(file));
            row.Children.Add(open); p.Children.Add(Ui.Card(row));
        }
        if ((WorkspaceService.Settings.RecentFiles?.Length ?? 0) == 0) p.Children.Add(Ui.Text("还没有最近项目。生成结果后点击“保存项目”，即可在这里找到。", 14, true));
        return Ui.Page(Ui.Heading("最近项目", "本机保存的创作，随时接着做。"), new ScrollViewer { Content = p });
    }
    private UIElement SettingsPage() => new Pages.SettingsPage();
}
