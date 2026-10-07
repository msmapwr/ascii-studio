using System.Text;
using System.Diagnostics;
using System.Drawing.Imaging;
using Charloom.Core;
using Charloom.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Charloom.Controls;

public sealed class ResultPane : Grid
{
    private static readonly TimeSpan GeneratedMergeWindow = TimeSpan.FromMilliseconds(900);
    private readonly TextBox editor;
    private readonly TextBlock stats;
    private readonly ViewportPreview viewport = new() { Visibility = Visibility.Collapsed };
    private ScrollViewer imageScroll => viewport.Scroll;
    private readonly Grid editorHost = new();
    private readonly StackPanel statusPanels = Ui.Stack(4);
    private readonly StackPanel pagingBar = new() { Orientation = Orientation.Horizontal, Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock pageLabel = Ui.Text("", 12);
    private readonly NumberBox rowJump = new() { Minimum = 1, Maximum = 1, Value = 1, Width = 88, Header = "跳到行" };
    private readonly ComboBox comparisonMode = Ui.Choice(["结果", "原图", "并排", "分界线"]);
    private readonly ComboBox comparisonSource = Ui.Choice(["裁剪/方向后", "原始图片"]);
    private readonly CheckBox comparisonSync = new() { Content = "同步缩放和相对滚动位置", IsChecked = true };
    private DocumentViewIndex? viewIndex;
    private int editorPage, editorVersion, documentVersion;
    private Task editorUpdateTask = Task.CompletedTask;
    private int fitMode;
    private bool loadingEditorPage;
    private readonly ToggleSwitch colorToggle = new() { Header = "图像预览", IsOn = false };
    private readonly ComboBox format = Ui.Choice(["TXT", "PNG", "JPEG", "GIF", "HTML", "SVG", "ANSI", "JSON", "Markdown"]);
    private readonly NumberBox fontSize = new NumberBox() { Minimum = 8, Maximum = 30, Value = 13, Width = 90, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly ComboBox exportScale = Ui.Choice(["1×", "2×", "3×", "4×"]);
    private readonly TextBlock exportDimensions = Ui.Text("生成后显示图片分辨率", 12, true);
    private readonly CommandBar toolbar = new() { DefaultLabelPosition = CommandBarDefaultLabelPosition.Right, IsDynamicOverflowEnabled = true };
    private bool updating;
    private XamlRoot? previewRoot;
    private int previewDensity = 1;
    private StudioSettings? appliedSettings;
    private readonly DispatcherTimer recoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer zoomSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly TextBlock zoomLabel = Ui.Text("100%", 12);
    private double zoom = 1;
    private double readableScale = 1;
    private readonly FontPicker characterFont = new("ResultFont", true);
    private readonly BoundedHistory<CreationSnapshot> history = new(snapshot => snapshot.EstimateBytes());
    private readonly SemaphoreSlim historyGate = new(1, 1);
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly AppBarButton undoButton, redoButton;
    private readonly InfoBar protection = new() { Title = "结果已手工编辑", IsClosable = false, Severity = InfoBarSeverity.Informational };
    private CreationSnapshot? candidate;
    private bool restoring;
    private long lastEdit;
    private string? editGroup;
    private long lastGeneratedPush;
    private bool lastPushWasGenerated;
    private string recoveryId = Guid.NewGuid().ToString("N");
    private string? projectPath;
    private bool isDirty;
    private long dirtyRevision;
    public Func<StudioProject, Task>? RestoreProject { get; set; }
    private readonly TextBox generatedPreview = new() { IsReadOnly = true, AcceptsReturn = true, Height = 240, TextWrapping = TextWrapping.NoWrap };
    public string CharacterFontFamily => characterFont.SelectedFont;
    public event Action<string>? CharacterFontChanged;
    public event Action<bool>? DirtyChanged;
    public event Action<AsciiDocument>? DocumentChanged;
    public AsciiDocument? Document { get; private set; }
    public bool IsDirty => isDirty;
    public string? ProjectPath => projectPath;
    public Func<AsciiDocument, StudioProject>? ProjectFactory { get; set; }
    public Func<AsciiDocument, StudioProject>? DraftFactory { get; set; }
    public void ShowColorPreview(bool enabled) => colorToggle.IsOn = enabled;

    public void SetSession(string id, string? path)
    {
        recoveryId = id;
        projectPath = path;
    }

    public void MarkRecovered()
    {
        MarkDirty();
        UpdateStats("已从恢复文件恢复");
    }

    public void InputChanged()
    {
        if (restoring) return;
        MarkDirty(); recoveryTimer.Stop(); recoveryTimer.Start();
    }

    public ResultPane()
    {
        RowSpacing = 12;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        AddAction("复制", Symbol.Copy, "Button_复制", async () => { await FlushEditor(); if (Document is not null) { var package = new DataPackage(); package.SetText(Document.Text); Clipboard.SetContent(package); App.Window.Message("已复制到剪贴板"); } });
        AddAction("保存项目", Symbol.Save, "Button_保存项目", async () => await SaveProjectAsync());
        AddAction("另存为", Symbol.Save, "ProjectSaveAs", async () => await SaveProjectAsync(true));
        AddAction("导出", Symbol.Download, "Button_导出", Export);
        undoButton = AddAction("撤销", Symbol.Undo, "ResultUndo", () => RestoreHistory(false));
        redoButton = AddAction("重做", Symbol.Redo, "ResultRedo", () => RestoreHistory(true));
        undoButton.IsEnabled = redoButton.IsEnabled = false;
        var undoKey = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Z, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        undoKey.Invoked += async (_, args) => { args.Handled = true; await App.Window.Guard(() => RestoreHistory(false)); };
        var redoKey = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Y, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        redoKey.Invoked += async (_, args) => { args.Handled = true; await App.Window.Guard(() => RestoreHistory(true)); };
        KeyboardAccelerators.Add(undoKey); KeyboardAccelerators.Add(redoKey);
        var protectionActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var accept = Ui.AsyncButton("更新当前结果", AcceptCandidate);
        var version = Ui.AsyncButton("生成新版本", SaveCandidate);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(accept, "ResultAcceptCandidate");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(version, "ResultSaveCandidate");
        protectionActions.Children.Add(accept); protectionActions.Children.Add(version);
        protectionActions.Children.Add(Ui.Button("保留当前结果", () => { candidate = null; protection.IsOpen = false; }));
        protection.Content = protectionActions;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(protection, "ResultProtection");
        var exportSettings = Ui.Stack();
        exportSettings.Width = 300;
        exportSettings.Children.Add(Ui.Field("导出格式", format));
        exportSettings.Children.Add(Ui.Field("图片导出倍率", exportScale));
        exportSettings.Children.Add(exportDimensions);
        AddSettings("导出设置", exportSettings, "ExportSettings");
        var displaySettings = Ui.Stack(); displaySettings.Width = 240;
        displaySettings.Children.Add(Ui.Field("字号 · 同时用于图片导出", fontSize));
        displaySettings.Children.Add(colorToggle);
        displaySettings.Children.Add(Ui.Field("字符画字体", characterFont));
        displaySettings.Children.Add(Ui.Text("Unicode 网格：中文与 emoji 占两列，组合字符保持完整，歧义符号占一列。图像预览与 SVG 使用固定列位；纯文本在不同终端中的字形宽度可能不同。Tab 展开为四列制表位。", 12, true));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(colorToggle, "ResultImagePreview");
        AddSettings("显示", displaySettings, "DisplaySettings");
        var viewSettings = Ui.Stack(); viewSettings.Width = 300;
        viewSettings.Children.Add(Ui.Button("适应窗口", () => Fit(2)));
        viewSettings.Children.Add(Ui.Button("适应宽度", () => Fit(1)));
        viewSettings.Children.Add(Ui.Button("定位到选区", LocateSelection));
        viewSettings.Children.Add(Ui.Text("超大作品使用抽样概览；手动缩放仍为 25%–400%。文字超过 20 万单元时按页编辑，保存和导出始终使用完整作品。", 12, true));
        AddSettings("视图", viewSettings, "ViewportSettings");
        var compareSettings = Ui.Stack(); compareSettings.Width = 300;
        compareSettings.Children.Add(Ui.Field("对比方式", comparisonMode));
        compareSettings.Children.Add(Ui.Field("对比图像", comparisonSource));
        compareSettings.Children.Add(comparisonSync);
        var splitSlider = Ui.Slider(5, 95, 50);
        compareSettings.Children.Add(Ui.Field("分界线位置", splitSlider));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(comparisonMode, "ComparisonMode");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(comparisonSource, "ComparisonSource");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(comparisonSync, "ComparisonSync");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(splitSlider, "ComparisonSplit");
        comparisonMode.IsEnabled = false;
        comparisonMode.SelectionChanged += (_, _) => { if (comparisonMode.SelectedIndex > 0) colorToggle.IsOn = true; UpdateComparisonMode(); };
        comparisonSource.SelectionChanged += (_, _) => viewport.SelectSource(comparisonSource.SelectedIndex == 0);
        comparisonSync.Checked += (_, _) => viewport.SetSync(true); comparisonSync.Unchecked += (_, _) => viewport.SetSync(false);
        splitSlider.ValueChanged += (_, _) => viewport.SetSplit(splitSlider.Value / 100);
        viewport.SplitChanged += fraction => splitSlider.Value = fraction * 100;
        AddSettings("原图对比", compareSettings, "ComparisonSettings");
        AddCommentSettings();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(generatedPreview, "GeneratedPreview");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(generatedPreview, "生成时的原始结果");
        AddSettings("原始结果", generatedPreview, "GeneratedSettings");
        var commands = Ui.Stack(8); commands.Children.Add(toolbar); commands.Children.Add(statusPanels); commands.Children.Add(protection); Children.Add(commands);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(exportScale, "ExportScale");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(exportScale, "图片导出倍率");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(exportDimensions, "ExportDimensions");
        exportScale.SelectionChanged += (_, _) => UpdateDimensions();
        editor = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(20), PlaceholderText = "转换结果", HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        characterFont.Changed += async family =>
        {
            if (!updating) await FlushEditor();
            editor.FontFamily = new FontFamily(family + ", Microsoft YaHei UI, Segoe UI Emoji");
            if (Document is not null && !updating) { var metrics = FontCatalog.Measure(family); Document = Document with { FontFamily = family, CellWidth = metrics.Width, CellHeight = metrics.Height }; RecordEdit("font", false); UpdateDimensions(); recoveryTimer.Stop(); recoveryTimer.Start(); }
            CharacterFontChanged?.Invoke(family);
            if (colorToggle.IsOn) await App.Window.Guard(RenderPreview);
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(editor, "ResultEditor");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor, "字符画结果编辑器");
        editor.TextChanged += (_, _) =>
        {
            var text = editor.Text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (updating || loadingEditorPage || text == EditorText() || (Document is null && text.Length == 0)) return;
            editorUpdateTask = App.Window.Guard(() => ApplyEditorText(text));
        };
        editorHost.RowDefinitions.Add(new() { Height = GridLength.Auto }); editorHost.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        pagingBar.Children.Add(Ui.AsyncButton("上一页", async () => { await FlushEditor(); LoadEditorPage(editorPage - 1); }));
        pagingBar.Children.Add(Ui.AsyncButton("下一页", async () => { await FlushEditor(); LoadEditorPage(editorPage + 1); }));
        pagingBar.Children.Add(pageLabel); pagingBar.Children.Add(rowJump);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(pageLabel, "ResultPageLabel");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(rowJump, "ResultRowJump");
        rowJump.ValueChanged += async (_, _) =>
        {
            if (loadingEditorPage || viewIndex is null || !double.IsFinite(rowJump.Value)) return;
            await App.Window.Guard(async () => { await FlushEditor(); var row = Math.Clamp((int)rowJump.Value - 1, 0, Math.Max(0, viewIndex.RowStarts.Length - 1)); if (viewIndex.RowStarts.Length > 0) { LoadEditorPage(viewIndex.PageAt(viewIndex.RowStarts[row])); editor.Select(Math.Clamp(viewIndex.RowStarts[row] - viewIndex.Pages[editorPage].Start, 0, editor.Text.Length), 0); } });
        };
        Grid.SetRow(editor, 1); editorHost.Children.Add(pagingBar); editorHost.Children.Add(editor);
        var canvas = new Grid(); canvas.Children.Add(editorHost); canvas.Children.Add(viewport);
        viewport.SizeChanged += (_, _) => { UpdateComparisonMode(); if (fitMode > 0) Fit(fitMode); };
        var card = Ui.Card(canvas, new Thickness(0)); Grid.SetRow(card, 1); Children.Add(card);
        var footer = new Grid { ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        stats = Ui.Text("尚未生成结果", 12, true); stats.TextTrimming = TextTrimming.CharacterEllipsis; stats.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(stats);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(stats, "ResultStats");
        viewport.Rendered += message => { if (colorToggle.IsOn) stats.Text = $"{Document?.Width} × {Document?.Height} · {message}"; };
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var zoomControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        Button ZoomButton(string text, string id, string name, Action action)
        {
            var button = Ui.Button(text, action);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, id);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
            Ui.ToolTip(button, name); return button;
        }
        zoomControls.Children.Add(ZoomButton("−", "ResultZoomOut", "缩小预览", () => SetZoom(zoom / 1.1)));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(zoomLabel, "ResultZoomValue");
        zoomLabel.VerticalAlignment = VerticalAlignment.Center; zoomControls.Children.Add(zoomLabel);
        zoomControls.Children.Add(ZoomButton("＋", "ResultZoomIn", "放大预览", () => SetZoom(zoom * 1.1)));
        zoomControls.Children.Add(ZoomButton("100%", "ResultZoomReset", "恢复预览缩放", () => SetZoom(1)));
        Grid.SetColumn(zoomControls, 1); footer.Children.Add(zoomControls);
        canvas.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnPreviewWheel), true);
        zoomSaveTimer.Tick += async (_, _) => { zoomSaveTimer.Stop(); var value = zoom; await App.Window.Guard(() => WorkspaceService.UpdateSettings(s => s with { PreviewZoom = value })); };
        fontSize.ValueChanged += async (_, _) => { if (double.IsFinite(fontSize.Value)) { ApplyVisualZoom(); UpdateDimensions(); if (colorToggle.IsOn) await App.Window.Guard(RenderPreview); } };
        colorToggle.Toggled += async (_, _) => { viewport.Visibility = colorToggle.IsOn ? Visibility.Visible : Visibility.Collapsed; editorHost.Visibility = colorToggle.IsOn ? Visibility.Collapsed : Visibility.Visible; if (colorToggle.IsOn) await App.Window.Guard(RenderPreview); };
        Grid.SetRow(footer, 2); Children.Add(footer);
        recoveryTimer.Tick += async (_, _) => { recoveryTimer.Stop(); await App.Window.Guard(SaveRecoveryAsync); };
        Loaded += (_, _) =>
        {
            ApplySettings(WorkspaceService.Settings);
            WorkspaceService.SettingsChanged += ApplySettings;
            previewRoot = XamlRoot;
            if (previewRoot is not null) previewRoot.Changed += OnPreviewRootChanged;
            if (colorToggle.IsOn && previewDensity != (int)Math.Ceiling(previewRoot?.RasterizationScale ?? 1)) _ = App.Window.Guard(RenderPreview);
        };
        Unloaded += (_, _) =>
        {
            recoveryTimer.Stop();
            zoomSaveTimer.Stop();
            WorkspaceService.SettingsChanged -= ApplySettings;
            if (previewRoot is not null) previewRoot.Changed -= OnPreviewRootChanged;
            previewRoot = null;
        };
        ApplySettings(WorkspaceService.Settings);
    }

    private void ApplySettings(StudioSettings settings)
    {
        if (appliedSettings?.PreviewFontSize != settings.PreviewFontSize) fontSize.Value = settings.PreviewFontSize;
        if (appliedSettings?.DefaultExportFormat != settings.DefaultExportFormat) format.SelectedIndex = Enumerable.Range(0, format.Items.Count).FirstOrDefault(i => (format.Items[i] as ComboBoxItem)?.Tag?.ToString() == settings.DefaultExportFormat);
        if (appliedSettings?.ExportScale != settings.ExportScale) exportScale.SelectedIndex = settings.ExportScale - 1;
        if (appliedSettings?.PreviewZoom != settings.PreviewZoom) { zoom = settings.PreviewZoom; ApplyVisualZoom(); }
        editor.TextWrapping = settings.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, settings.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
        stats.Visibility = settings.ShowStats ? Visibility.Visible : Visibility.Collapsed;
        appliedSettings = settings;
    }

    public void SetReadablePreview(bool enabled)
    {
        readableScale = enabled ? 1.6 : 1;
        ApplyVisualZoom();
    }

    private void ApplyVisualZoom()
    {
        editor.FontSize = fontSize.Value * zoom * readableScale;
        zoomLabel.Text = $"{zoom * 100:0}%";
        viewport.SetScale((float)fontSize.Value, zoom * readableScale);
    }

    private void SetZoom(double value, Windows.Foundation.Point? pivot = null, bool automatic = false)
    {
        var scroll = colorToggle.IsOn ? imageScroll : Ui.FindDescendant<ScrollViewer>(editor);
        var previous = zoom;
        var x = scroll?.HorizontalOffset ?? 0; var y = scroll?.VerticalOffset ?? 0;
        if (!automatic) fitMode = 0;
        zoom = Math.Clamp(value, automatic ? .000001 : .25, 4); ApplyVisualZoom();
        if (scroll is not null)
        {
            var ratio = zoom / previous; var point = pivot ?? new Windows.Foundation.Point(0, 0);
            DispatcherQueue.TryEnqueue(() => { scroll.UpdateLayout(); scroll.ChangeView((x + point.X) * ratio - point.X, (y + point.Y) * ratio - point.Y, null, true); });
        }
        zoomSaveTimer.Stop(); if (!automatic) zoomSaveTimer.Start();
    }

    private string EditorText()
    {
        if (viewIndex is null) return Document?.Text ?? "";
        var range = viewIndex.Pages[Math.Clamp(editorPage, 0, viewIndex.Pages.Length - 1)];
        return viewIndex.Document.Text.Substring(range.Start, range.Length);
    }
    private async Task FlushEditor()
    {
        while (true) { var pendingEdit = editorUpdateTask; await pendingEdit; if (ReferenceEquals(pendingEdit, editorUpdateTask)) return; }
    }
    private async Task ApplyEditorText(string text)
    {
        var version = ++editorVersion; var previous = Document; var oldIndex = viewIndex; var page = editorPage;
        var rangeStart = oldIndex?.Pages[page].Start ?? 0;
        var caret = rangeStart + editor.SelectionStart;
        var family = CharacterFontFamily; var metrics = FontCatalog.Measure(family);
        MarkDirty();
        await Task.Delay(100);
        if (version != editorVersion) return;
        DocumentViewIndex next;
        try
        {
            next = await Task.Run(() =>
        {
            var complete = oldIndex?.IsPaged == true ? oldIndex.ReplacePage(page, text) : text;
            var document = AsciiDocument.FromText(complete, previous?.Title ?? "Untitled") with { FontFamily = family, CellWidth = metrics.Width, CellHeight = metrics.Height };
            return new DocumentViewIndex(document);
        });
        }
        catch (ArgumentException)
        {
            if (version == editorVersion && ReferenceEquals(Document, previous)) LoadEditorPage(page);
            throw;
        }
        if (version != editorVersion || !ReferenceEquals(Document, previous)) return;
        Document = next.Document; viewIndex = next; viewport.SetDocument(next);
        LoadEditorPage(next.PageAt(caret), caret, preserveInput: true);
        RecordEdit("text", true); WorkspaceService.CurrentArt = Document; UpdateStats("已手工编辑 · 颜色已重置");
        recoveryTimer.Stop(); recoveryTimer.Start();
    }
    private void LoadEditorPage(int page, int? selection = null, bool preserveInput = false)
    {
        if (viewIndex is null) return;
        editorPage = Math.Clamp(page, 0, viewIndex.Pages.Length - 1);
        loadingEditorPage = true; updating = true;
        try
        {
            var text = EditorText();
            var sameInput = preserveInput && TextUtilities.Normalize(editor.Text) == text;
            if (!sameInput) editor.Text = text;
            pagingBar.Visibility = viewIndex.IsPaged ? Visibility.Visible : Visibility.Collapsed;
            pageLabel.Text = $"{editorPage + 1}/{viewIndex.Pages.Length}";
            rowJump.Maximum = Math.Max(1, Document?.Height ?? 1); rowJump.Value = viewIndex.Pages[editorPage].FirstRow + 1;
            if (!sameInput && selection is { } offset) editor.Select(Math.Clamp(offset - viewIndex.Pages[editorPage].Start, 0, editor.Text.Length), 0);
        }
        finally { updating = false; loadingEditorPage = false; }
    }
    private void Fit(int mode)
    {
        var viewWidth = viewport.ActualWidth > 0 ? viewport.ActualWidth : editorHost.ActualWidth;
        var viewHeight = viewport.ActualHeight > 0 ? viewport.ActualHeight : editorHost.ActualHeight;
        if (Document is null || viewWidth < 1) return;
        fitMode = mode; colorToggle.IsOn = true;
        var metrics = FontCatalog.Measure(Document.FontFamily);
        var width = (Document.Width * metrics.Width * fontSize.Value / 13 + 40) * readableScale;
        var height = (Document.Height * metrics.Height * fontSize.Value / 13 + 40) * readableScale;
        var availableWidth = comparisonMode.SelectedIndex == 2 && viewWidth >= 640 ? viewWidth / 2 : viewWidth;
        var factor = Math.Max(1, availableWidth - 24) / Math.Max(1, width);
        if (mode == 2) factor = Math.Min(factor, Math.Max(1, viewHeight - 24) / Math.Max(1, height));
        SetZoom(factor, automatic: true);
        DispatcherQueue.TryEnqueue(() => viewport.Scroll.ChangeView(0, 0, null, true));
    }
    private void LocateSelection()
    {
        if (viewIndex is null || Document is null) return;
        var offset = viewIndex.Pages[editorPage].Start + editor.SelectionStart;
        var position = viewIndex.Position(offset); var metrics = FontCatalog.Measure(Document.FontFamily);
        colorToggle.IsOn = true;
        DispatcherQueue.TryEnqueue(() => viewport.Scroll.ChangeView(
            Math.Max(0, (20 + position.Column * metrics.Width * fontSize.Value / 13) * zoom * readableScale - 20),
            Math.Max(0, (20 + position.Row * metrics.Height * fontSize.Value / 13) * zoom * readableScale - 20), null, true));
    }
    private void UpdateComparisonMode()
    {
        var mode = comparisonMode.SelectedIndex;
        // Narrow layouts retain the user's comparison choice and show a source/result switch.
        viewport.SetMode(viewport.ActualWidth < 640 && mode is 2 or 3 ? 1 : mode);
    }
    public async Task SetComparisonSources(byte[]? original, byte[]? processed)
    {
        await viewport.SetSources(original, processed);
        comparisonMode.IsEnabled = original is not null;
        if (original is null) comparisonMode.SelectedIndex = 0;
        viewport.SelectSource(comparisonSource.SelectedIndex == 0);
    }

    private void OnPreviewWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (colorToggle.IsOn && viewport.HandleIndependentSourceWheel(e)) return;
        var scroll = colorToggle.IsOn ? viewport.ScrollAt(e.OriginalSource as DependencyObject) : Ui.FindDescendant<ScrollViewer>(editor);
        if (scroll is null) return;
        var pointer = e.GetCurrentPoint(scroll); var delta = pointer.Properties.MouseWheelDelta;
        if ((e.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) != 0)
        {
            SetZoom(zoom * Math.Pow(1.1, delta / 120d), pointer.Position); e.Handled = true;
        }
        else if ((e.KeyModifiers & Windows.System.VirtualKeyModifiers.Shift) != 0)
        {
            scroll.ChangeView(Math.Clamp(scroll.HorizontalOffset - delta / 2d, 0, scroll.ScrollableWidth), null, null, true); e.Handled = true;
        }
    }

    private async void OnPreviewRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (colorToggle.IsOn) await App.Window.Guard(RenderPreview);
    }

    public Flyout AddSettings(string label, FrameworkElement content, string automationId, bool useAvailableHeight = false)
    {
        var flyout = Ui.AdaptiveFlyout(content, anchor: useAvailableHeight ? null : toolbar, showClose: true);
        var button = new AppBarButton { Label = label, Icon = new SymbolIcon(Symbol.Setting) };
        // Close the overflow before opening an editor so two light-dismiss
        // surfaces cannot compete for focus or consume the first input click.
        button.Click += (_, _) =>
        {
            toolbar.IsOpen = false;
            flyout.ShowAt(toolbar, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                ShowMode = Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowMode.Standard,
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom
            });
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, automationId);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        toolbar.PrimaryCommands.Add(button);
        return flyout;
    }

    private AppBarButton AddAction(string label, Symbol icon, string automationId, Func<Task> action)
    {
        var button = new AppBarButton { Label = label, Icon = new SymbolIcon(icon) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, automationId);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await App.Window.Guard(action); } finally { button.IsEnabled = true; UpdateHistoryButtons(); } };
        toolbar.PrimaryCommands.Add(button);
        return button;
    }

    public void AddStatus(FrameworkElement content) => statusPanels.Children.Add(content);
    public bool HasManualEdits => history.HasCurrent && history.Current.Edited;
    private bool transientPreview;
    public async Task ShowTransientPreview(AsciiDocument document, CancellationToken cancellationToken = default)
    {
        var version = documentVersion;
        await FlushEditor();
        cancellationToken.ThrowIfCancellationRequested();
        if (HasManualEdits || version != documentVersion) return;
        transientPreview = true; viewport.SetDocument(new DocumentViewIndex(document));
        viewport.Visibility = Visibility.Visible; editorHost.Visibility = Visibility.Collapsed;
        ApplyVisualZoom(); viewport.Refresh();
    }
    public void ClearTransientPreview()
    {
        if (!transientPreview) return;
        transientPreview = false;
        if (viewIndex is not null) viewport.SetDocument(viewIndex);
        viewport.Visibility = colorToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        editorHost.Visibility = colorToggle.IsOn ? Visibility.Collapsed : Visibility.Visible;
    }
    public async Task SetDocument(AsciiDocument document, string suffix = "", bool preserveEdits = true, CancellationToken cancellationToken = default)
    {
        var version = ++documentVersion;
        await FlushEditor();
        var nextIndex = await Task.Run(() => new DocumentViewIndex(document, cancellationToken), cancellationToken);
        // Typing may continue while indexing a large generated document. Commit
        // it before evaluating edit protection; a newer generation wins.
        await FlushEditor();
        cancellationToken.ThrowIfCancellationRequested(); ClearTransientPreview();
        if (version != documentVersion) return;
        document = nextIndex.Document;
        if (!restoring)
        {
            var project = !preserveEdits && history.HasCurrent ? history.Current.Project with { Document = document } : ProjectFactory?.Invoke(document) ?? new StudioProject(1, document, null, null, null, "snapshot");
            var generated = !preserveEdits && history.HasCurrent ? history.Current.Generated : document;
            var next = await Task.Run(() => CreationSnapshot.Capture(project, !preserveEdits, generated), cancellationToken);
            await FlushEditor();
            cancellationToken.ThrowIfCancellationRequested();
            if (version != documentVersion) return;
            if (preserveEdits && history.HasCurrent && history.Current.Edited)
            {
                candidate = next; protection.Message = "新结果已准备好。可更新当前结果并保留撤销，或保存为独立版本。"; protection.IsOpen = true;
                return;
            }
            var now = Stopwatch.GetTimestamp();
            var merge = preserveEdits && history.HasCurrent && lastPushWasGenerated
                && Stopwatch.GetElapsedTime(lastGeneratedPush, now) <= GeneratedMergeWindow
                && CanMergeGenerated(history.Current.Project, next.Project);
            history.Push(next, merge);
            lastPushWasGenerated = preserveEdits;
            lastGeneratedPush = now;
            MarkDirty();
            editGroup = null; candidate = null; protection.IsOpen = false;
        }
        var scroll = colorToggle.IsOn ? imageScroll : Ui.FindDescendant<ScrollViewer>(editor);
        var offsetX = scroll?.HorizontalOffset ?? 0; var offsetY = scroll?.VerticalOffset ?? 0;
        var selection = (viewIndex?.Pages[editorPage].Start ?? 0) + editor.SelectionStart; var selectionLength = editor.SelectionLength;
        Document = document; WorkspaceService.CurrentArt = document; updating = true;
        var wasPaged = viewIndex?.IsPaged == true;
        viewIndex = nextIndex; viewport.SetDocument(nextIndex);
        try { characterFont.Select(document.FontFamily); editor.FontFamily = new FontFamily(CharacterFontFamily + ", Microsoft YaHei UI, Segoe UI Emoji"); LoadEditorPage(nextIndex.PageAt(selection), selection); }
        finally { updating = false; }
        if (nextIndex.IsPaged && !wasPaged) colorToggle.IsOn = true;
        UpdateStats(suffix); if (colorToggle.IsOn) await RenderPreview();
        if (fitMode > 0) Fit(fitMode);
        DocumentChanged?.Invoke(document);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(Document, document)) return;
            var localSelection = Math.Clamp(selection - nextIndex.Pages[editorPage].Start, 0, editor.Text.Length);
            editor.Select(localSelection, Math.Min(selectionLength, Math.Max(0, editor.Text.Length - localSelection)));
            if (fitMode == 0) scroll?.ChangeView(offsetX, offsetY, null, true);
        });
        UpdateHistoryButtons(); recoveryTimer.Stop(); if (!restoring) recoveryTimer.Start();
    }
    private void UpdateHistoryButtons()
    {
        if (undoButton is null || redoButton is null) return;
        undoButton.IsEnabled = history.CanUndo && !restoring; redoButton.IsEnabled = history.CanRedo && !restoring;
        if (history.HasCurrent && history.Current.Generated is { } original)
        {
            generatedPreview.Text = original.Text.Length > 64_000 ? "原始结果较大，以下为开头预览：\n" + DocumentViewIndex.Prefix(original.Text, 64_000) : original.Text; generatedPreview.FontFamily = new FontFamily(original.FontFamily + ", Microsoft YaHei UI, Segoe UI Emoji");
        }
    }

    private StudioProject CurrentProject()
    {
        if (Document is null) throw new InvalidOperationException("尚无结果。");
        return history.HasCurrent ? history.Current.Project with { Document = Document } : ProjectFactory?.Invoke(Document) ?? new(1, Document, null, null, null, "snapshot");
    }

    private StudioProject ProjectForSave()
    {
        var document = Document ?? AsciiDocument.FromText("");
        var project = (DraftFactory?.Invoke(document) ?? CurrentProject()) with { Version = WorkspaceService.CurrentProjectVersion };
        return history.HasCurrent ? project with { Edited = history.Current.Edited, GeneratedDocument = history.Current.Generated } : project;
    }

    public async Task LoadDocument(StudioProject project)
    {
        project = project with { Document = UnicodeGrid.Upgrade(project.Document), GeneratedDocument = project.GeneratedDocument is null ? null : UnicodeGrid.Upgrade(project.GeneratedDocument) };
        if (restoring) { await SetDocument(project.Document); return; }
        var snapshot = CreationSnapshot.Capture(project, project.Edited, project.GeneratedDocument ?? project.Document);
        restoring = true;
        try { await SetDocument(project.Document); }
        finally { restoring = false; }
        history.Push(snapshot); candidate = null; protection.IsOpen = false; editGroup = null; lastPushWasGenerated = false; isDirty = false; DirtyChanged?.Invoke(false); DocumentChanged?.Invoke(project.Document); UpdateHistoryButtons(); recoveryTimer.Stop(); recoveryTimer.Start();
        if (snapshot.Edited) UpdateStats("已恢复手工编辑");
    }

    private void RecordEdit(string group, bool edited)
    {
        if (restoring || Document is null) return;
        lastPushWasGenerated = false;
        MarkDirty();
        if (!history.HasCurrent) history.Push(CreationSnapshot.Capture(new(1, AsciiDocument.FromText(""), null, null, null, "snapshot"), false, null));
        var previous = history.Current;
        WorkspaceService.CurrentArt = Document;
        var next = CreationSnapshot.Capture(previous.Project with { Document = Document }, edited || previous.Edited, previous.Generated);
        var now = Environment.TickCount64;
        history.Push(next, editGroup == group && now - lastEdit < 500);
        editGroup = group; lastEdit = now; UpdateHistoryButtons();
    }

    private void MarkDirty()
    {
        dirtyRevision++;
        if (isDirty) return;
        isDirty = true;
        DirtyChanged?.Invoke(true);
    }

    private static bool CanMergeGenerated(StudioProject previous, StudioProject next)
    {
        if (!string.Equals(previous.Mode, next.Mode, StringComparison.Ordinal)
            || !string.Equals(previous.SourceImage, next.SourceImage, StringComparison.Ordinal)
            || !string.Equals(previous.SourceText, next.SourceText, StringComparison.Ordinal)) return false;

        // Only merge consecutive parameter-generated states that still point to
        // the same input. Imported sources and document edits remain boundaries.
        return next.Mode is "image" or "text" or "generator" or "ansi";
    }

    private async Task RestoreHistory(bool redo)
    {
        await FlushEditor();
        await historyGate.WaitAsync();
        try
        {
            if (redo ? !history.CanRedo : !history.CanUndo) return;
            var previous = history.Current; var target = redo ? history.PeekRedo() : history.PeekUndo();
            restoring = true; editor.IsReadOnly = true; UpdateHistoryButtons();
            try
            {
                if (RestoreProject is not null && target.Project.Mode != "snapshot") await RestoreProject(target.Project);
                else await SetDocument(target.Project.Document);
                if (redo) history.Redo(); else history.Undo();
                MarkDirty();
                candidate = null; protection.IsOpen = false; editGroup = null; lastPushWasGenerated = false;
                UpdateStats(target.Edited ? "已手工编辑" : redo ? "已重做" : "已撤销");
            }
            catch
            {
                if (RestoreProject is not null && previous.Project.Mode != "snapshot") await RestoreProject(previous.Project);
                else await SetDocument(previous.Project.Document);
                throw;
            }
            finally { restoring = false; editor.IsReadOnly = false; UpdateHistoryButtons(); recoveryTimer.Stop(); recoveryTimer.Start(); }
        }
        finally { historyGate.Release(); }
    }

    private async Task AcceptCandidate()
    {
        var next = candidate ?? throw new ArgumentException("没有等待应用的新结果。");
        await historyGate.WaitAsync();
        try
        {
            restoring = true;
            try
            {
                if (RestoreProject is not null) await RestoreProject(next.Project);
                else await SetDocument(next.Project.Document);
                history.Push(next); editGroup = null; lastPushWasGenerated = false; candidate = null; protection.IsOpen = false; UpdateStats("已更新 · 可撤销");
                MarkDirty();
            }
            finally { restoring = false; UpdateHistoryButtons(); recoveryTimer.Stop(); recoveryTimer.Start(); }
        }
        finally { historyGate.Release(); }
    }

    private async Task SaveCandidate()
    {
        var next = candidate ?? throw new ArgumentException("没有等待保存的新结果。");
        var picker = new FileSavePicker { SuggestedFileName = SafeName(next.Project.Document.Title) + "-version", SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add(GuiText.Translate("Charloom 项目"), [".asciiproj"]); App.Window.InitializePicker(picker);
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        await WorkspaceService.SaveProject(file.Path, next.Project);
        App.Window.Message("新版本已保存；当前手工编辑结果仍保留。可从作品库打开新版本。");
    }
    private void AddCommentSettings()
    {
        var content = Ui.Stack(); content.Width = 420;
        var language = Ui.Choice(CommentTools.Languages.Select(l => l.Name));
        var style = Ui.Choice(["优先行注释", "块注释"]);
        var sample = new TextBox { AcceptsReturn = true, IsReadOnly = true, Height = 180, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(sample, "CommentPreview");
        content.Children.Add(Ui.Field("注释语言", language)); content.Children.Add(Ui.Field("注释形式", style)); content.Children.Add(sample);
        AsciiDocument? original = null, wrapped = null;
        string? prepared = null;
        void Prepare()
        {
            if (Document is null) throw new ArgumentException("先生成或输入一些内容。");
            if (Document != wrapped) original = Document;
            var text = CommentTools.Wrap(original!.Text, Ui.ChoiceValue(language) ?? "C", style.SelectedIndex == 1);
            sample.Text = text.Length > 64000 ? DocumentViewIndex.Prefix(text, 64000) + "\n（预览节选，复制和替换使用完整内容）" : text; prepared = text;
        }
        void Invalidate() { prepared = null; sample.Text = ""; }
        language.SelectionChanged += (_, _) => Invalidate(); style.SelectionChanged += (_, _) => Invalidate();
        content.Children.Add(Ui.AsyncButton("生成注释预览", async () => { await FlushEditor(); Prepare(); }));
        content.Children.Add(Ui.AsyncButton("复制注释", async () =>
        {
            await FlushEditor(); Prepare(); var package = new DataPackage(); package.SetText(prepared!); Clipboard.SetContent(package); App.Window.Message("已复制注释");
        }));
        content.Children.Add(Ui.AsyncButton("替换结果", async () =>
        {
            await FlushEditor();
            Prepare();
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "用注释替换结果？", Content = "本次原文可通过“恢复注释前原文”恢复。", PrimaryButtonText = "替换", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            if (await Ui.ShowDialog(dialog) != ContentDialogResult.Primary) return;
            var next = AsciiDocument.FromText(prepared!, original!.Title) with { FontFamily = original.FontFamily, CellWidth = original.CellWidth, CellHeight = original.CellHeight };
            await SetDocument(next, "已套注释", preserveEdits: false); wrapped = Document;
        }));
        content.Children.Add(Ui.AsyncButton("恢复注释前原文", async () =>
        {
            if (original is null || Document != wrapped) throw new ArgumentException("当前结果没有可恢复的注释原文。");
            await SetDocument(original, preserveEdits: false); wrapped = null; Invalidate();
        }));
        AddSettings("注释", content, "CommentSettings");
    }

    public async Task SaveRecoveryAsync()
    {
        await FlushEditor();
        await saveGate.WaitAsync();
        try
        {
            if ((Document is null && DraftFactory is null) || restoring || (!isDirty && projectPath is not null)) return;
            var snapshot = ProjectForSave();
            var bytes = await Task.Run(() => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot));
            if (bytes.Length > ProjectFileService.MaximumBytes) throw new InvalidDataException("恢复项目超过 256MB，请降低字符画尺寸或输入图片大小。");
            await WorkspaceService.AtomicWrite(Path.Combine(WorkspaceService.DataDirectory, "recovery.asciiproj"), bytes);
            await WorkspaceService.AtomicWrite(WorkspaceSessionService.RecoveryPath(recoveryId), bytes);
        }
        finally { saveGate.Release(); }
    }

    private void UpdateStats(string suffix = "")
    {
        stats.Text = Document is null ? "尚未生成结果" : $"{Document.Width} × {Document.Height} · {Document.Text.Length:N0} 字符{(suffix.Length > 0 ? " · " + suffix : "")}";
        UpdateDimensions();
    }
    private void UpdateDimensions()
    {
        if (Document is null) return;
        try { var size = ImagingService.RenderSize(Document, (float)fontSize.Value, scale: exportScale.SelectedIndex + 1); exportDimensions.Text = $"{size.Width} × {size.Height} px · PNG / JPEG / GIF"; }
        catch (ArgumentException ex) { exportDimensions.Text = ex.Message; }
    }
    private async Task RenderPreview()
    {
        if (Document is null) return;
        if (!ReferenceEquals(viewIndex?.Document, Document)) { var document = Document; var next = await Task.Run(() => new DocumentViewIndex(document)); if (!ReferenceEquals(Document, document)) return; viewIndex = next; viewport.SetDocument(next); }
        previewDensity = Math.Max(1, (int)Math.Ceiling(XamlRoot?.RasterizationScale ?? 1));
        ApplyVisualZoom(); viewport.Refresh();
    }

    public async Task<bool> SaveProjectAsync(bool saveAs = false)
    {
        await FlushEditor();
        if (Document is null && DraftFactory is null) { App.Window.Message("先生成或输入一些内容。"); return false; }
        var path = saveAs ? null : projectPath;
        if (path is null)
        {
            var picker = new FileSavePicker { SuggestedFileName = SafeName(Document?.Title ?? "Untitled"), SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeChoices.Add(GuiText.Translate("Charloom 项目"), [".asciiproj"]); App.Window.InitializePicker(picker);
            var file = await picker.PickSaveFileAsync(); if (file is null) return false;
            path = file.Path;
        }
        await saveGate.WaitAsync();
        try
        {
            var snapshot = ProjectForSave();
            var savedRevision = dirtyRevision;
            await WorkspaceService.SaveProject(path, snapshot);
            projectPath = path; isDirty = dirtyRevision != savedRevision; DirtyChanged?.Invoke(isDirty);
            if (!isDirty) { recoveryTimer.Stop(); WorkspaceSessionService.DeleteRecovery(recoveryId); }
        }
        finally { saveGate.Release(); }
        App.Window.Message("项目已保存，包含当前字符画和可用的输入素材。");
        return true;
    }

    private async Task Export()
    {
        await FlushEditor();
        if (Document is null) { App.Window.Message("先生成或输入一些内容。"); return; }
        var doc = Document; var kind = Ui.ChoiceValue(format) ?? "TXT";
        var ext = kind switch { "JPEG" => ".jpg", "ANSI" => ".ans", "Markdown" => ".md", _ => "." + kind.ToLowerInvariant() };
        var picker = new FileSavePicker { SuggestedFileName = SafeName(WorkspaceService.Settings.FilePrefix + doc.Title), SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add(kind, [ext]); App.Window.InitializePicker(picker);
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        var size = (float)fontSize.Value; var scale = exportScale.SelectedIndex + 1;
        var bytes = await Task.Run(() => kind switch
        {
            "PNG" => ImagingService.Render(doc, size, scale: scale),
            "JPEG" => ImagingService.Render(doc, size, format: ImageFormat.Jpeg, scale: scale),
            "GIF" => ImagingService.Render(doc, size, format: ImageFormat.Gif, scale: scale),
            "HTML" => Encoding.UTF8.GetBytes(ExportService.Html(doc)),
            "SVG" => Encoding.UTF8.GetBytes(ExportService.Svg(doc)),
            "ANSI" => Encoding.UTF8.GetBytes(ExportService.Ansi(doc)),
            "JSON" => Encoding.UTF8.GetBytes(ExportService.Json(doc)),
            "Markdown" => Encoding.UTF8.GetBytes(ExportService.Markdown(doc)),
            _ => Encoding.UTF8.GetBytes(doc.Text.Replace("\n", "\r\n"))
        });
        await WorkspaceService.AtomicWrite(file.Path, bytes); App.Window.Message($"已导出 {kind}：{Path.GetFileName(file.Path)}");
    }
    private static string SafeName(string? title)
    {
        var name = new string((title ?? "Untitled").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).Take(80).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(name) ? "Untitled" : name;
    }
}
