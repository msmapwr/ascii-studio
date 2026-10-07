using Charloom.Controls;
using Charloom.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using System.Runtime.InteropServices;

namespace Charloom;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, UIElement> pages = [];
    private readonly Dictionary<string, ProjectTabState> projectTabs = [];
    private readonly List<ProjectSessionEntry> recoveryEntries = [];
    private string? activeSessionId;
    private bool restoringSession;
    private bool closingWindow;
    private bool allowClose;
    private bool showingCloseDialog;
    private WindowPlacement? normalPlacement;
    private bool placementMaximized;
    private int themeVersion;
    private string? appliedUiFont;
    private double appliedUiSize;
    private string? appliedLanguage;
    private sealed class ProjectTabState(TabViewItem tab, IProjectSessionPage page, ProjectSessionEntry entry)
    {
        public TabViewItem Tab { get; } = tab;
        public IProjectSessionPage Page { get; } = page;
        public ProjectSessionEntry Entry { get; set; } = entry;
        public bool IsDirty { get; set; }
    }
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    public MainWindow()
    {
        InitializeComponent(); Title = ProductIdentity.DisplayName + " — " + ProductIdentity.Subtitle;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Charloom.ico"));
        var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Min((int)(1280 * scale), work.Width - 40), Math.Min((int)(820 * scale), work.Height - 40)));
        AppWindow.Move(new Windows.Graphics.PointInt32(work.X + (work.Width - AppWindow.Size.Width) / 2, work.Y + (work.Height - AppWindow.Size.Height) / 2));
        RestorePlacement();
        UpdateMinimumSize(GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d);
        CapturePlacement();
        AppWindow.Changed += (_, _) => CapturePlacement();
        AppWindow.Closing += OnAppClosing;
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
        ProjectTabs.SelectedItem = WorkspaceTab;
        var closeTabKey = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.F4, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        closeTabKey.Invoked += (sender, args) =>
        {
            args.Handled = true;
            if (activeSessionId is { } id && projectTabs.TryGetValue(id, out var tab)) _ = Guard(() => CloseProjectTabAsync(tab.Tab));
        };
        Root.KeyboardAccelerators.Add(closeTabKey);
        WorkspaceService.SettingsChanged += OnSettingsChanged;
        Root.Loaded += (_, _) => { ApplyUiFont(); ApplyLanguage(); };
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
        ApplyLanguage();
        ApplyDensity();
        ApplyUiFont();
        var theme = settings.Theme switch { "Light" => ElementTheme.Light, "System" => ElementTheme.Default, _ => ElementTheme.Dark };
        await Guard(() => ChangeTheme(theme));
    }

    private void ApplyLanguage()
    {
        var language = GuiText.ResolveLanguage(WorkspaceService.Settings.UiLanguage);
        if (appliedLanguage == language) return;
        appliedLanguage = language;
        Root.Language = language;
        Navigation.OpenPaneLength = Math.Clamp((language == "en-US" ? 260 : 210) * WorkspaceService.Settings.UiFontSize / 14, 210, 380);
        UiLocalization.Attach(Root);
        foreach (var item in Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>()) UiLocalization.Attach(item);
        if (Navigation.SettingsItem is NavigationViewItem settingsItem)
        { settingsItem.Content = "设置"; UiLocalization.Attach(settingsItem); }
        UiLocalization.Watch(WorkspaceTab, TabViewItem.HeaderProperty);
        UiLocalization.Refresh();
        Title = ProductIdentity.DisplayName + " — " + GuiText.Translate(ProductIdentity.Subtitle);
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
        Navigation.OpenPaneLength = Math.Clamp((GuiText.ResolveLanguage(settings.UiLanguage) == "en-US" ? 260 : 210) * settings.UiFontSize / 14, 210, 380);
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
        if (args.SelectedItem is null && !args.IsSettingsSelected) return;
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
            if (key is "image" or "text" or "ansi" or "generators")
            {
                ProjectTabs.SelectedItem = WorkspaceTab;
                CreateProjectTab(key);
                return;
            }
            ProjectTabs.SelectedItem = WorkspaceTab;
            if (key is "library" or "settings") pages.Remove(key);
            if (!pages.TryGetValue(key, out var page))
            {
                page = key switch
                {
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

    private static IProjectSessionPage CreateProjectPage(string mode) => mode switch
    {
        "image" => new Pages.ImagePage(),
        "text" => new Pages.TextPage(),
        "ansi" => new Pages.AnsiPage(),
        "generator" or "generators" => new Pages.GeneratorPage(),
        _ => new SnapshotProjectPage()
    };

    private static string ModeLabel(string mode) => mode switch
    { "image" => "图片创作", "text" => "文字创作", "ansi" => "ANSI 创作", "generator" or "generators" => "生成器创作", _ => "字符画" };

    private void OnAddProjectTab(TabView sender, object args)
    {
        var mode = (Navigation.SelectedItem as NavigationViewItem)?.Tag?.ToString();
        if (mode is null && activeSessionId is { } id && projectTabs.TryGetValue(id, out var current)) mode = current.Entry.Mode;
        CreateProjectTab(mode is "image" or "text" or "ansi" or "generator" or "generators" ? mode : "image");
    }

    private void CreateProjectTab(string mode)
    {
        var id = Guid.NewGuid().ToString("N");
        var page = CreateProjectPage(mode);
        page.SetSession(id, null);
        AddProjectTab(page, new(id, page.SessionMode, null, ModeLabel(mode)), false);
    }

    private void AddProjectTab(IProjectSessionPage page, ProjectSessionEntry entry, bool dirty)
    {
        if (projectTabs.TryGetValue(entry.Id, out var existing))
        {
            ProjectTabs.SelectedItem = existing.Tab;
            return;
        }
        if (projectTabs.Count >= 32) throw new InvalidOperationException("最多同时打开 32 个项目，请先保存并关闭部分项目。");
        entry = entry with { Title = entry.Title.Length > 128 ? entry.Title[..128] : entry.Title };
        var tab = new TabViewItem { IsClosable = true, Content = page };
        var state = new ProjectTabState(tab, page, entry) { IsDirty = dirty };
        Ui.ToolTip(tab, entry.Path ?? "未保存项目");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tab, "ProjectTab_" + entry.Id[..Math.Min(8, entry.Id.Length)]);
        projectTabs[entry.Id] = state;
        page.DirtyChanged += changed =>
        {
            state.IsDirty = changed;
            if (!changed && page.ResultPane.ProjectPath is { } savedPath) state.Entry = state.Entry with { Path = savedPath };
            UpdateProjectTab(state); _ = PersistSessionAsync();
        };
        page.DocumentChanged += document =>
        {
            var title = string.IsNullOrWhiteSpace(document.Title) ? ModeLabel(state.Entry.Mode) : document.Title;
            state.Entry = state.Entry with { Title = title.Length > 128 ? title[..128] : title };
            UpdateProjectTab(state); _ = PersistSessionAsync();
        };
        UpdateProjectTab(state);
        ProjectTabs.TabItems.Add(tab);
        ProjectTabs.SelectedItem = tab;
        activeSessionId = entry.Id;
        recoveryEntries.RemoveAll(item => item.Id == entry.Id);
        _ = PersistSessionAsync();
    }

    private static void UpdateProjectTab(ProjectTabState state)
    {
        state.Tab.Header = (state.IsDirty ? "● " : "") + state.Entry.Title;
        Ui.ToolTip(state.Tab, state.Entry.Path ?? "尚未保存到文件");
    }

    private void OnProjectTabSelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs args)
    {
        if (ProjectTabs is null) return;
        if (activeSessionId is { } priorId && projectTabs.TryGetValue(priorId, out var prior))
            _ = Guard(prior.Page.SaveRecoveryAsync);
        if (ProjectTabs.SelectedItem is TabViewItem tab)
        {
            var selected = projectTabs.Values.FirstOrDefault(state => ReferenceEquals(state.Tab, tab));
            activeSessionId = selected?.Entry.Id;
            if (selected is not null) Navigation.SelectedItem = null;
            _ = PersistSessionAsync();
        }
    }

    private async void OnProjectTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) =>
        await Guard(() => CloseProjectTabAsync(args.Tab));

    private async Task CloseProjectTabAsync(TabViewItem tab)
    {
        if (showingCloseDialog || closingWindow) return;
        var state = projectTabs.Values.FirstOrDefault(item => ReferenceEquals(item.Tab, tab));
        if (state is null) return;
        var keepRecovery = false;
        if (state.IsDirty)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "保存项目更改？",
                Content = state.Entry.Title,
                PrimaryButtonText = "保存",
                SecondaryButtonText = "保留恢复",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };
            showingCloseDialog = true;
            ContentDialogResult choice;
            try { choice = await Ui.ShowDialog(dialog, localizeContent: false); }
            finally { showingCloseDialog = false; }
            if (choice == ContentDialogResult.None) return;
            if (choice == ContentDialogResult.Primary)
            {
                if (!await state.Page.SaveProjectAsync() || state.Page.ResultPane.IsDirty) return;
            }
            else
            {
                await state.Page.SaveRecoveryAsync(); keepRecovery = true;
                recoveryEntries.RemoveAll(item => item.Id == state.Entry.Id);
                recoveryEntries.Add(state.Entry with { Path = state.Page.ResultPane.ProjectPath });
            }
        }
        if (!keepRecovery) WorkspaceSessionService.DeleteRecovery(state.Entry.Id);
        projectTabs.Remove(state.Entry.Id);
        ProjectTabs.TabItems.Remove(tab);
        if (activeSessionId == state.Entry.Id) activeSessionId = null;
        _ = PersistSessionAsync();
        pages.Remove("home");
    }

    private void OnAppClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (allowClose) return;
        args.Cancel = true;
        if (closingWindow || showingCloseDialog) return;
        closingWindow = true;
        DispatcherQueue.TryEnqueue(() => _ = Guard(ConfirmWindowCloseAsync));
    }

    private async Task ConfirmWindowCloseAsync()
    {
        try
        {
            var dirty = projectTabs.Values.Where(state => state.IsDirty).ToArray();
            if (dirty.Length > 0)
            {
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "保存未保存的项目？", Content = $"还有 {dirty.Length} 个项目未保存。", PrimaryButtonText = "全部保存", SecondaryButtonText = "保留恢复并退出", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
                var choice = await Ui.ShowDialog(dialog);
                if (choice == ContentDialogResult.None) return;
                foreach (var state in dirty)
                {
                    if (choice == ContentDialogResult.Primary) { if (!await state.Page.SaveProjectAsync() || state.Page.ResultPane.IsDirty) return; }
                    else await state.Page.SaveRecoveryAsync();
                }
            }
            await PersistSessionAsync();
            allowClose = true;
            Close();
        }
        finally { closingWindow = false; }
    }

    private Task PersistSessionAsync()
    {
        if (restoringSession) return Task.CompletedTask;
        var entries = projectTabs.Values.Select(state => state.Entry).ToArray();
        return Guard(() => WorkspaceSessionService.Save(entries, activeSessionId, recoveryEntries.ToArray()));
    }

    public async Task RestoreSessionAsync()
    {
        var saved = WorkspaceSessionService.Load();
        recoveryEntries.Clear(); recoveryEntries.AddRange(saved.Recoveries ?? []);
        var recovered = false;
        restoringSession = true;
        try
        {
            foreach (var entry in saved.Tabs)
            {
                try
                {
                    var count = projectTabs.Count;
                    await OpenSavedSession(entry);
                    recovered |= projectTabs.Count > count && projectTabs[entry.Id].IsDirty;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
                { await Log(ex); }
            }
            if (saved.ActiveId is { } active && projectTabs.TryGetValue(active, out var selected)) ProjectTabs.SelectedItem = selected.Tab;
        }
        finally { restoringSession = false; }
        await PersistSessionAsync();
        if (recovered) Message("已恢复上次未保存的项目；保存后可更新原项目文件。");
        pages.Remove("home");
        if ((Navigation.SelectedItem as NavigationViewItem)?.Tag?.ToString() == "home")
        { pages["home"] = HomePage(); PageHost.Content = pages["home"]; }
    }

    private async Task OpenSavedSession(ProjectSessionEntry entry)
    {
        StudioProject? project = null;
        var recovered = false;
        if (entry.Path is { } path && File.Exists(path))
        {
            project = await WorkspaceService.OpenProject(path);
            var recoveryPath = WorkspaceSessionService.RecoveryPath(entry.Id);
            if (File.Exists(recoveryPath) && File.GetLastWriteTimeUtc(recoveryPath) > File.GetLastWriteTimeUtc(path)
                && WorkspaceSessionService.TryLoadRecovery(entry.Id, out var recovery) && recovery is not null)
            { project = recovery; recovered = true; }
        }
        else if (WorkspaceSessionService.TryLoadRecovery(entry.Id, out var recovery) && recovery is not null)
        { project = recovery; recovered = true; }
        if (project is null)
        {
            if (entry.Path is not null) return;
            var blankPage = CreateProjectPage(entry.Mode);
            blankPage.SetSession(entry.Id, null);
            AddProjectTab(blankPage, entry, false);
            return;
        }
        var page = CreateProjectPage(entry.Path is null ? project.Mode : entry.Mode);
        page.SetSession(entry.Id, entry.Path);
        await page.LoadProject(project);
        if (recovered) page.ResultPane.MarkRecovered();
        AddProjectTab(page, entry with { Mode = page.SessionMode, Title = project.Document.Title }, recovered);
    }

    private async Task OpenRecovery(ProjectSessionEntry entry)
    {
        if (!WorkspaceSessionService.TryLoadRecovery(entry.Id, out var project) || project is null)
        { Message("恢复文件不可用。", true); return; }
        var page = CreateProjectPage(entry.Mode); page.SetSession(entry.Id, entry.Path); await page.LoadProject(project); page.ResultPane.MarkRecovered();
        AddProjectTab(page, entry with { Title = project.Document.Title }, true);
        Message("恢复项目已打开；检查内容后保存即可继续编辑。");
    }

    private UIElement HomePage()
    {
        var content = Ui.Stack(24);
        var welcome = Ui.Stack(12);
        welcome.Children.Add(Ui.Text(ProductIdentity.Tagline, 25));
        var letters = TextFontLibrary.Render("builtin:small", ProductIdentity.Name, new()).TrimEnd('\n').Split('\n');
        var cat = new[] { " /\\_/\\ ", "( o.o )", " > ^ < " };
        var art = new TextBlock { Text = string.Join('\n', Enumerable.Range(0, letters.Length).Select(row => (row < cat.Length ? cat[row] : "").PadRight(12) + letters[row])), FontSize = 17, LineHeight = 22, TextWrapping = TextWrapping.NoWrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(art, "HomeWelcomeArt");
        art.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"); welcome.Children.Add(art);
        welcome.SizeChanged += (_, _) => art.Visibility = welcome.ActualWidth < 700 ? Visibility.Collapsed : Visibility.Visible;
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
        foreach (var recovery in recoveryEntries.ToArray())
        {
            var savedRecovery = recovery;
            open.Children.Add(Ui.AsyncButton("恢复：" + recovery.Title, () => OpenRecovery(savedRecovery)));
        }
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
        var fullPath = Path.GetFullPath(path);
        var existing = projectTabs.Values.FirstOrDefault(state => state.Entry.Path is { } openPath
            && string.Equals(Path.GetFullPath(openPath), fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { ProjectTabs.SelectedItem = existing.Tab; return; }
        var project = await WorkspaceService.OpenProject(fullPath);
        var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant())))[..24].ToLowerInvariant();
        var recovered = false;
        var recoveryPath = WorkspaceSessionService.RecoveryPath(id);
        if (File.Exists(recoveryPath) && File.GetLastWriteTimeUtc(recoveryPath) > File.GetLastWriteTimeUtc(fullPath)
            && WorkspaceSessionService.TryLoadRecovery(id, out var recovery) && recovery is not null)
        { project = recovery; recovered = true; }
        var page = CreateProjectPage(project.Mode); page.SetSession(id, fullPath); await page.LoadProject(project);
        if (recovered) page.ResultPane.MarkRecovered();
        AddProjectTab(page, new(id, page.SessionMode, fullPath, project.Document.Title), recovered);
        Message(recovered ? "项目已打开并恢复上次未保存的修改。" : "项目已打开。");
    }
    private UIElement LibraryPage()
    {
        var p = Ui.Stack(12); p.Children.Add(Ui.AsyncButton("打开项目", PickProject, true));
        foreach (var path in WorkspaceService.Settings.RecentFiles ?? [])
        {
            var file = path; var row = Ui.Stack(6); row.Children.Add(Ui.Text(Path.GetFileNameWithoutExtension(file), 17, localize: false)); row.Children.Add(Ui.Text(file, 12, true, localize: false));
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
