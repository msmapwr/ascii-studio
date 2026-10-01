using System.Text;
using System.Drawing.Imaging;
using AsciiStudio.Core;
using AsciiStudio.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace AsciiStudio.Controls;

public sealed class ResultPane : Grid
{
    private readonly TextBox editor;
    private readonly TextBlock stats;
    private readonly Image preview = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer imageScroll;
    private readonly ToggleSwitch colorToggle = new() { Header = "图像预览", IsOn = false };
    private readonly ComboBox format = Ui.Choice(["TXT", "PNG", "JPEG", "GIF", "HTML", "SVG", "ANSI", "JSON", "Markdown"]);
    private readonly NumberBox fontSize = new NumberBox() { Minimum = 8, Maximum = 30, Value = 13, Width = 90, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly ComboBox exportScale = Ui.Choice(["1×", "2×", "3×", "4×"]);
    private readonly TextBlock exportDimensions = Ui.Text("生成后显示图片分辨率", 12, true);
    private readonly CommandBar toolbar = new() { DefaultLabelPosition = CommandBarDefaultLabelPosition.Right, IsDynamicOverflowEnabled = true };
    private bool updating;
    private int renderVersion;
    private XamlRoot? previewRoot;
    private int previewDensity = 1;
    private StudioSettings? appliedSettings;
    private readonly DispatcherTimer recoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer zoomSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly TextBlock zoomLabel = Ui.Text("100%", 12);
    private double zoom = 1;
    private double readableScale = 1;
    private double previewWidth, previewHeight;
    public AsciiDocument? Document { get; private set; }
    public Func<AsciiDocument, StudioProject>? ProjectFactory { get; set; }

    public ResultPane()
    {
        RowSpacing = 12;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        AddAction("复制", Symbol.Copy, "Button_复制", () => { if (Document is not null) { var package = new DataPackage(); package.SetText(Document.Text); Clipboard.SetContent(package); App.Window.Message("已复制到剪贴板"); } return Task.CompletedTask; });
        AddAction("保存项目", Symbol.Save, "Button_保存项目", SaveProject);
        AddAction("导出", Symbol.Download, "Button_导出", Export);
        var exportSettings = Ui.Stack();
        exportSettings.Width = 300;
        exportSettings.Children.Add(Ui.Field("导出格式", format));
        exportSettings.Children.Add(Ui.Field("图片导出倍率", exportScale));
        exportSettings.Children.Add(exportDimensions);
        AddSettings("导出设置", exportSettings, "ExportSettings");
        var displaySettings = Ui.Stack(); displaySettings.Width = 240;
        displaySettings.Children.Add(Ui.Field("字号 · 同时用于图片导出", fontSize));
        displaySettings.Children.Add(colorToggle);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(colorToggle, "ResultImagePreview");
        AddSettings("显示", displaySettings, "DisplaySettings");
        Children.Add(toolbar);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(exportScale, "ExportScale");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(exportScale, "图片导出倍率");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(exportDimensions, "ExportDimensions");
        exportScale.SelectionChanged += (_, _) => UpdateDimensions();
        editor = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(20), PlaceholderText = "转换结果", HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(editor, "ResultEditor");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor, "字符画结果编辑器");
        editor.TextChanged += (_, _) =>
        {
            var text = editor.Text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (updating || text == Document?.Text || (Document is null && text.Length == 0)) return;
            try { Document = AsciiDocument.FromText(text, Document?.Title ?? "Untitled"); UpdateStats("已编辑 · 颜色已重置"); recoveryTimer.Stop(); recoveryTimer.Start(); }
            catch (ArgumentException ex) { updating = true; editor.Text = Document?.Text ?? ""; updating = false; App.Window.Message(ex.Message, true); }
        };
        var canvas = new Grid(); canvas.Children.Add(editor);
        imageScroll = new ScrollViewer { Content = preview, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
        canvas.Children.Add(imageScroll); var card = Ui.Card(canvas, new Thickness(0)); Grid.SetRow(card, 1); Children.Add(card);
        var footer = new Grid { ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        stats = Ui.Text("尚未生成结果", 12, true); stats.TextTrimming = TextTrimming.CharacterEllipsis; stats.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(stats);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(stats, "ResultStats");
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var zoomControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        Button ZoomButton(string text, string id, string name, Action action)
        {
            var button = Ui.Button(text, action);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, id);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
            ToolTipService.SetToolTip(button, name); return button;
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
        colorToggle.Toggled += async (_, _) => { imageScroll.Visibility = colorToggle.IsOn ? Visibility.Visible : Visibility.Collapsed; editor.Visibility = colorToggle.IsOn ? Visibility.Collapsed : Visibility.Visible; if (colorToggle.IsOn) await App.Window.Guard(RenderPreview); };
        Grid.SetRow(footer, 2); Children.Add(footer);
        recoveryTimer.Tick += async (_, _) => { recoveryTimer.Stop(); await App.Window.Guard(SaveRecovery); };
        Loaded += (_, _) =>
        {
            ApplySettings(WorkspaceService.Settings);
            WorkspaceService.SettingsChanged += ApplySettings;
            previewRoot = XamlRoot;
            if (previewRoot is not null) previewRoot.Changed += OnPreviewRootChanged;
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
        if (appliedSettings?.DefaultExportFormat != settings.DefaultExportFormat) format.SelectedItem = settings.DefaultExportFormat;
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
        if (preview.Source is not null)
        {
            preview.Width = previewWidth * zoom * readableScale;
            preview.Height = previewHeight * zoom * readableScale;
        }
    }

    private void SetZoom(double value, Windows.Foundation.Point? pivot = null)
    {
        var scroll = colorToggle.IsOn ? imageScroll : Ui.FindDescendant<ScrollViewer>(editor);
        var previous = zoom;
        var x = scroll?.HorizontalOffset ?? 0; var y = scroll?.VerticalOffset ?? 0;
        zoom = Math.Clamp(value, .25, 4); ApplyVisualZoom();
        if (scroll is not null)
        {
            var ratio = zoom / previous; var point = pivot ?? new Windows.Foundation.Point(0, 0);
            DispatcherQueue.TryEnqueue(() => { scroll.UpdateLayout(); scroll.ChangeView((x + point.X) * ratio - point.X, (y + point.Y) * ratio - point.Y, null, true); });
        }
        zoomSaveTimer.Stop(); zoomSaveTimer.Start();
    }

    private void OnPreviewWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var scroll = colorToggle.IsOn ? imageScroll : Ui.FindDescendant<ScrollViewer>(editor);
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
        if (colorToggle.IsOn && previewDensity != (int)Math.Ceiling(sender.RasterizationScale)) await App.Window.Guard(RenderPreview);
    }

    public void AddSettings(string label, FrameworkElement content, string automationId)
    {
        var flyout = Ui.AdaptiveFlyout(content, anchor: toolbar);
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
    }

    private void AddAction(string label, Symbol icon, string automationId, Func<Task> action)
    {
        var button = new AppBarButton { Label = label, Icon = new SymbolIcon(icon) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, automationId);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await App.Window.Guard(action); } finally { button.IsEnabled = true; } };
        toolbar.PrimaryCommands.Add(button);
    }

    public async Task SetDocument(AsciiDocument document, string suffix = "")
    {
        document.Validate(); Document = document; updating = true; editor.Text = document.Text; updating = false;
        UpdateStats(suffix); if (colorToggle.IsOn) await RenderPreview();
        recoveryTimer.Stop(); recoveryTimer.Start();
    }
    private async Task SaveRecovery()
    {
        if (Document is null) return;
        var snapshot = ProjectFactory?.Invoke(Document) ?? new StudioProject(1, Document, null, null, null, "snapshot");
        var bytes = await Task.Run(() => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot));
        if (bytes.Length > 100_000_000) throw new InvalidDataException("恢复项目超过 100MB，请降低字符画尺寸或输入图片大小。");
        await WorkspaceService.AtomicWrite(Path.Combine(WorkspaceService.DataDirectory, "recovery.asciiproj"), bytes);
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
        var current = ++renderVersion; var document = Document; var size = (float)fontSize.Value;
        var density = Math.Max(1, (int)Math.Ceiling(XamlRoot?.RasterizationScale ?? 1));
        var dimensions = ImagingService.RenderSize(document, size, scale: density);
        var bytes = await Task.Run(() => ImagingService.Render(document, size, scale: density));
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
        stream.Seek(0); var source = new BitmapImage(); await source.SetSourceAsync(stream);
        if (current == renderVersion)
        {
            previewDensity = density;
            preview.Stretch = Stretch.Fill;
            previewWidth = dimensions.Width / (double)density;
            previewHeight = dimensions.Height / (double)density;
            preview.Source = source;
            ApplyVisualZoom();
        }
    }

    private async Task SaveProject()
    {
        if (Document is null) { App.Window.Message("先生成或输入一些内容。"); return; }
        var picker = new FileSavePicker { SuggestedFileName = SafeName(Document.Title), SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add("AsciiStudio 项目", [".asciiproj"]); App.Window.InitializePicker(picker);
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        await WorkspaceService.SaveProject(file.Path, ProjectFactory?.Invoke(Document) ?? new(1, Document, null, null, null, "snapshot"));
        App.Window.Message("项目已保存，包含当前字符画和可用的输入素材。");
    }

    private async Task Export()
    {
        if (Document is null) { App.Window.Message("先生成或输入一些内容。"); return; }
        var doc = Document; var kind = format.SelectedItem?.ToString() ?? "TXT";
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
