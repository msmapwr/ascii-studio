using Charloom.Controls;
using Charloom.Core;
using Charloom.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Charloom.Pages;

public sealed class ImagePage : Grid, IProjectSessionPage
{
    private readonly ResultPane result = new();
    private readonly Image thumbnail = new() { Height = 120, Stretch = Stretch.Uniform };
    private readonly TextBlock sourceInfo = Ui.Text("PNG · JPEG · BMP · GIF · TIFF", 12, true);
    private readonly ProgressRing progress = new() { Width = 20, Height = 20, IsActive = false };
    private readonly NumberBox columns = new NumberBox() { Minimum = 8, Maximum = 2000, Value = 120, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly NumberBox rows = new NumberBox() { Minimum = 1, Maximum = 2000, Value = 60, IsEnabled = false, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly CheckBox autoRows = new() { Content = "保持原图比例（自动计算行数）", IsChecked = true };
    private readonly ComboBox resolution = Ui.Choice(["标准 · 120 列", "精细 · 240 列", "高清 · 480 列", "超清 · 960 列", "原图像素尺寸", "自定义"]);
    private readonly Slider cellAspect = Ui.Slider(.25, 1, .5, .05);
    private readonly ComboBox ramp = Ui.Choice(["标准", "精细", "极简", "字母", "字母数字", "箭头", "CP437", "扩展高密度", "灰阶", "数学符号", "标准 2", "数字", "最大", "黑白", "自定义"]);
    private readonly TextBox characters = new() { Text = " .:-=+*#%@", MaxLength = 200 };
    private readonly Slider brightness = Ui.Slider(.1, 2.5, 1, .05), contrast = Ui.Slider(.1, 3, 1, .05), gamma = Ui.Slider(.2, 3, 1, .05);
    private readonly Slider saturation = Ui.Slider(0, 2, 1, .05), hue = Ui.Slider(0, 360, 0), gray = Ui.Slider(0, 1, 0, .05), sepia = Ui.Slider(0, 1, 0, .05), sharpness = Ui.Slider(0, 5, 0, .1);
    private readonly CheckBox invert = new() { Content = "反转亮暗" }, color = new() { Content = "保留原图颜色" }, edges = new() { Content = "边缘检测" }, threshold = new() { Content = "二值化" };
    private readonly NumberBox thresholdValue = new NumberBox() { Minimum = 0, Maximum = 255, Value = 128 };
    private readonly ComboBox dither = Ui.Choice(["关闭", "Floyd–Steinberg", "Jarvis–Judice–Ninke", "Stucki", "Atkinson"]);
    private readonly ImageGeometryEditor geometry = new();
    private readonly CheckBox fontAspect = new() { Content = "按字体实际宽高补偿比例", IsChecked = true };
    private readonly ComboBox artStyle = Ui.Choice(["密度字符", "结构线条", "Braille 点阵", "半块双色"]);
    private readonly CheckBox measuredDensity = new() { Content = "按所选字体实测字符密度", IsChecked = true };
    private readonly Slider adaptive = Ui.Slider(0, 1, 0, .05), structureThreshold = Ui.Slider(0, 1, .12, .01);
    private readonly CheckBox preserveAlpha = new() { Content = "透明区域留空" }, trimAlpha = new() { Content = "裁去透明边缘" };
    private readonly NumberBox alphaThreshold = new() { Minimum = 1, Maximum = 255, Value = 16 };
    private readonly TextBox background = new() { Text = "#FFFFFF", MaxLength = 7 };
    private readonly ComboBox palette = Ui.Choice(["原图颜色", "双色", "自定义颜色", "渐变", "ANSI 16 色", "ANSI 256 色", "限制颜色数量"]);
    private readonly TextBox paletteColors = new() { Text = "#172554,#FBBF24", MaxLength = 1024 };
    private readonly NumberBox paletteSize = new() { Minimum = 2, Maximum = 64, Value = 16 };
    private readonly CheckBox quickPreview = new() { Content = "调整时先显示低成本预览", IsChecked = true };
    private readonly TextBlock processingStatus = Ui.Text("", 12, true);
    private readonly TextBlock cacheStatus = Ui.Text("缓存：每项目64MB / 全局256MB", 12, true);
    private readonly Button cancel = new() { Content = "取消转换" };
    private readonly ImageCreationController controller = new();
    private ImagePipelineService pipeline => controller.Pipeline;
    private ConversionOptions lastOptions { get => controller.LastOptions; set => controller.LastOptions = value; }
    private bool suspend;
    public ResultPane ResultPane => result;
    public string SessionMode => "image";
    public event Action<bool>? DirtyChanged { add => result.DirtyChanged += value; remove => result.DirtyChanged -= value; }
    public event Action<AsciiDocument>? DocumentChanged { add => result.DocumentChanged += value; remove => result.DocumentChanged -= value; }
    public void SetSession(string id, string? path) => result.SetSession(id, path);
    public Task<bool> SaveProjectAsync() => result.SaveProjectAsync();
    public Task SaveRecoveryAsync() => result.SaveRecoveryAsync();
    public ImagePage()
    {
        result.RestoreProject = LoadProject;
        var input = Ui.Stack();
        var import = Ui.Stack(10); import.Children.Add(thumbnail); import.Children.Add(sourceInfo);
        import.Children.Add(Ui.AsyncButton("选择图片 / 拖放到此处", PickImage, true));
        import.Children.Add(Ui.AsyncButton("从剪贴板粘贴", Paste)); input.Children.Add(Ui.Card(import));
        input.Children.Add(Ui.Field("输出分辨率预设", resolution));
        input.Children.Add(Ui.Field("转换风格", artStyle));
        input.Children.Add(Ui.AsyncButton("转换", () => controller.Source.Source is null ? throw new InvalidOperationException("请先选择图片。") : ConvertAsync(), true));
        progress.Visibility = Visibility.Collapsed;
        cancel.Visibility = Visibility.Collapsed; cancel.IsEnabled = false;
        cancel.Click += (_, _) => CancelConversion();
        KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape && controller.Operations.IsRunning) { CancelConversion(); e.Handled = true; } };
        var statusPanel = new Grid { ColumnSpacing = 8 };
        statusPanel.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        statusPanel.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        statusPanel.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(processingStatus, 1); Grid.SetColumn(cancel, 2);
        statusPanel.Children.Add(progress); statusPanel.Children.Add(processingStatus); statusPanel.Children.Add(cancel); result.AddStatus(statusPanel);
        var sizeSettings = Ui.Stack();
        sizeSettings.Children.Add(Ui.SettingsGrid(Ui.Field("字符网格宽度（列）", columns), Ui.Field("字符网格高度（行）", rows)));
        sizeSettings.Children.Add(Ui.WithHelp(autoRows, "保持原图比例（自动计算行数）"));
        sizeSettings.Children.Add(Ui.WithHelp(fontAspect, "按字体实际宽高补偿比例"));
        sizeSettings.Children.Add(Ui.Field("字符宽高比 · 默认 0.5", cellAspect));
        sizeSettings.Children.Add(Ui.Text("最高 2000 × 2000 字符。字体补偿保留图像比例；自定义固定宽高可能拉伸。原图尺寸按处理图片和 13px 字体换算，像素尺寸为近似值。", 12, true));
        result.AddSettings("分辨率", sizeSettings, "ImageSizeSettings");
        var characterSettings = Ui.Stack(); characterSettings.Width = 300;
        characterSettings.Children.Add(Ui.Field("字符风格", ramp));
        characterSettings.Children.Add(Ui.Field("字符集 · 由浅到深", characters));
        characterSettings.Children.Add(Ui.Field("抖动算法", dither));
        characterSettings.Children.Add(Ui.WithHelp(measuredDensity, "按所选字体实测字符密度"));
        result.AddSettings("字符", characterSettings, "ImageCharacterSettings");
        var adjustments = Ui.Stack();
        adjustments.Children.Add(Ui.SettingsGrid(Ui.Field("亮度", brightness), Ui.Field("对比度", contrast), Ui.Field("Gamma", gamma), Ui.Field("饱和度", saturation), Ui.Field("色相", hue), Ui.Field("灰度", gray), Ui.Field("棕褐色", sepia), Ui.Field("锐化", sharpness), Ui.WithHelp(color, "保留原图颜色"), Ui.WithHelp(invert, "反转亮暗")));
        adjustments.Children.Add(Ui.Field("自适应 · 平坦区域降噪 / 边缘增强", adaptive));
        result.AddSettings("画面", adjustments, "ImageAdjustmentSettings");
        var effects = Ui.Stack(); effects.Width = 300;
        effects.Children.Add(Ui.WithHelp(edges, "边缘检测")); effects.Children.Add(Ui.WithHelp(threshold, "二值化")); effects.Children.Add(Ui.Field("阈值", thresholdValue));
        effects.Children.Add(Ui.Field("结构线条检测阈值", structureThreshold));
        var reset = Ui.Button("重置全部图片参数", Reset); Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(reset, "ImageReset"); effects.Children.Add(reset);
        result.AddSettings("效果", effects, "ImageEffectSettings");
        result.AddSettings("裁剪与方向", geometry, "ImageGeometrySettings");
        var paletteSettings = Ui.Stack(); paletteSettings.Width = 300;
        paletteSettings.Children.Add(Ui.Field("透明合成背景 · #RRGGBB", background));
        paletteSettings.Children.Add(Ui.WithHelp(preserveAlpha, "透明区域留空"));
        paletteSettings.Children.Add(Ui.WithHelp(trimAlpha, "裁去透明边缘"));
        paletteSettings.Children.Add(Ui.Field("透明判定阈值 · 1–255", alphaThreshold));
        paletteSettings.Children.Add(Ui.Field("调色板", palette));
        paletteSettings.Children.Add(Ui.Field("颜色 · #RRGGBB，用逗号分隔", paletteColors));
        paletteSettings.Children.Add(Ui.Field("渐变 / 限色数量 · 2–64", paletteSize));
        result.AddSettings("透明与颜色", paletteSettings, "ImagePaletteSettings");
        var performance = Ui.Stack(); performance.Width = 300;
        performance.Children.Add(Ui.WithHelp(quickPreview, "调整时先显示低成本预览"));
        performance.Children.Add(cacheStatus);
        var clearCache = Ui.Button("清空本项目转换缓存", () => { pipeline.Clear(); cacheStatus.Text = "已清空本项目转换缓存"; });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(clearCache, "ImageClearCache"); performance.Children.Add(clearCache);
        performance.Children.Add(Ui.Text("最多400万采样点。Braille每字符8点，半块每字符2点。缓存预算不含当前原图和正在处理的数据。", 12, true));
        result.AddSettings("性能", performance, "ImagePerformanceSettings");
        foreach (var (control, id) in new (DependencyObject, string)[] { (artStyle, "ImageArtStyle"), (measuredDensity, "ImageMeasuredDensity"), (adaptive, "ImageAdaptive"), (structureThreshold, "ImageStructureThreshold"), (preserveAlpha, "ImagePreserveAlpha"), (trimAlpha, "ImageTrimAlpha"), (alphaThreshold, "ImageAlphaThreshold"), (background, "ImageBackground"), (palette, "ImagePalette"), (paletteColors, "ImagePaletteColors"), (paletteSize, "ImagePaletteSize"), (quickPreview, "ImageQuickPreview"), (cancel, "ImageCancel"), (processingStatus, "ImageProcessingStatus"), (cacheStatus, "ImageCacheStatus") })
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(control, id);
        artStyle.SelectionChanged += (_, _) => { if (!suspend && artStyle.SelectedIndex == 3) color.IsChecked = true; UpdateModeControls(); Queue(); };
        color.Checked += (_, _) => UpdateModeControls(); color.Unchecked += (_, _) => UpdateModeControls();
        UpdateModeControls();
        palette.SelectionChanged += (_, _) => { paletteColors.IsEnabled = palette.SelectedIndex is 1 or 2 or 3; paletteSize.IsEnabled = palette.SelectedIndex is 3 or 6; if (!suspend && palette.SelectedIndex > 0) color.IsChecked = true; Queue(); };
        paletteColors.IsEnabled = false; paletteSize.IsEnabled = false;
        foreach (var checkbox in new[] { measuredDensity, preserveAlpha, trimAlpha, quickPreview }) { checkbox.Checked += (_, _) => Queue(); checkbox.Unchecked += (_, _) => Queue(); }
        adaptive.ValueChanged += (_, _) => Queue(); structureThreshold.ValueChanged += (_, _) => Queue();
        alphaThreshold.ValueChanged += (_, _) => Queue(); paletteSize.ValueChanged += (_, _) => Queue();
        trimAlpha.Checked += (_, _) => RefreshRegion(); trimAlpha.Unchecked += (_, _) => RefreshRegion();
        alphaThreshold.ValueChanged += (_, _) => RefreshRegion();
        background.TextChanged += (_, _) => Queue(); paletteColors.TextChanged += (_, _) => Queue();
        result.CharacterFontChanged += _ => { if (!suspend) Queue(); };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(fontAspect, "ImageFontAspect");
        fontAspect.Checked += (_, _) => { cellAspect.IsEnabled = false; Queue(); };
        fontAspect.Unchecked += (_, _) => { cellAspect.IsEnabled = autoRows.IsChecked == true; Queue(); };
        cellAspect.IsEnabled = false;
        geometry.Changed += async () => await App.Window.Guard(async () =>
        {
            controller.Operations.Cancel();
            await RefreshThumbnail(); if (IsLoaded) Queue();
        });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(sourceInfo, "ImageSourceInfo");
        Unloaded += (_, _) => { controller.Operations.Cancel(); controller.Loads.Cancel(); result.ClearTransientPreview(); controller.Source.LoadVersion++; controller.Source.ThumbnailVersion++; };
        var body = Ui.Page(Ui.Heading("图片转换", "把照片变成字符画。调整滑块，细节即刻变化。"), Ui.Workspace(input, result)); Children.Add(body);
        AllowDrop = true;
        DragOver += (_, e) => { if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Bitmap)) e.AcceptedOperation = DataPackageOperation.Copy; };
        Drop += async (_, e) => await App.Window.Guard(async () =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems)) { var files = await e.DataView.GetStorageItemsAsync(); if (files.FirstOrDefault() is StorageFile file) await LoadFile(file); }
            else if (e.DataView.Contains(StandardDataFormats.Bitmap)) await LoadBitmapReference(await e.DataView.GetBitmapAsync());
        });
        ramp.SelectionChanged += (_, _) =>
        {
            if (ramp.SelectedIndex == 14) return;
            characters.Text = ramp.SelectedIndex switch
            {
                1 => " .`^\",:;Il!i~+_-?][}{1)(|\\/tfjrxnuvczXYUJCLQ0OZmwqpdbkhao*#MW&8%B@$",
                2 => " .o@",
                3 => " abcdefghijklmnopqrstuvwxyz",
                4 => " 123abcABCxyzXYZ@",
                5 => " .↖↑↗←→↙↓↘",
                6 => " ░▒▓█",
                7 => " .:░▒▓█",
                8 => " ▁▂▃▄▅▆▇█",
                9 => " .∴∵≈≡∞∑∏",
                10 => " .,:;ox%#@",
                11 => " 0123456789",
                12 => " .:-=+*#%@█",
                13 => " █",
                _ => " .:-=+*#%@"
            };
        };
        columns.ValueChanged += (_, _) => { if (!suspend) resolution.SelectedIndex = 5; Queue(); }; thresholdValue.ValueChanged += (_, _) => Queue();
        rows.ValueChanged += (_, _) => { if (!suspend) resolution.SelectedIndex = 5; Queue(); }; cellAspect.ValueChanged += (_, _) => Queue();
        autoRows.Checked += (_, _) => { rows.IsEnabled = false; cellAspect.IsEnabled = fontAspect.IsChecked != true; Queue(); }; autoRows.Unchecked += (_, _) => { rows.IsEnabled = true; cellAspect.IsEnabled = false; Queue(); };
        resolution.SelectionChanged += (_, _) =>
        {
            if (resolution.SelectedIndex is < 0 or > 4) return;
            suspend = true; autoRows.IsChecked = true; suspend = false;
            if (resolution.SelectedIndex < 4) { suspend = true; columns.Value = new[] { 120, 240, 480, 960 }[resolution.SelectedIndex]; autoRows.IsChecked = true; suspend = false; }
            Queue();
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(columns, "ImageColumns");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(rows, "ImageRows");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(autoRows, "ImageAutoRows");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(resolution, "ImageResolution");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(brightness, "ImageBrightness");
        foreach (var s in new[] { brightness, contrast, gamma, saturation, hue, gray, sepia, sharpness }) s.ValueChanged += (_, _) => Queue();
        foreach (var box in new[] { invert, color, edges, threshold }) { box.Checked += (_, _) => Queue(); box.Unchecked += (_, _) => Queue(); }
        dither.SelectionChanged += (_, _) => Queue(); characters.TextChanged += (_, _) => Queue();
        result.ProjectFactory = doc => controller.Project(doc, lastOptions, controller.Source.Encoded, fontAspect.IsChecked == true, resolution.SelectedIndex, geometry.Current);
        result.DraftFactory = doc => controller.Project(doc, Options(), controller.Source.Encoded, fontAspect.IsChecked == true, resolution.SelectedIndex, geometry.Current);
        suspend = true; columns.Value = WorkspaceService.Settings.DefaultColumns; resolution.SelectedIndex = columns.Value == 120 ? 0 : 5; suspend = false;
    }

    private ConversionOptions Options()
    {
        var metrics = FontCatalog.Measure(result.CharacterFontFamily);
        var aspect = fontAspect.IsChecked == true ? metrics.Width / metrics.Height : cellAspect.Value;
        var count = ReadDimension(columns, "列数");
        if (resolution.SelectedIndex == 4 && controller.Source.Source is not null)
        {
            var crop = geometry.Current; var w = controller.Source.Decoded.width * crop.Width / 100; var h = controller.Source.Decoded.height * crop.Height / 100;
            if (crop.QuarterTurns % 2 != 0) (w, h) = (h, w);
            count = Math.Clamp((int)Math.Round(w / metrics.Width), 8, 2000);
            count = Math.Max(8, Math.Min(count, (int)Math.Floor(2000 * w / h / aspect)));
        }
        return new()
        {
            Columns = count,
            Rows = autoRows.IsChecked == true ? 0 : ReadDimension(rows, "行数"),
            CellAspect = aspect,
            Characters = characters.Text,
            Brightness = brightness.Value,
            Contrast = contrast.Value,
            Gamma = gamma.Value,
            Saturation = saturation.Value,
            Hue = hue.Value,
            Grayscale = gray.Value,
            Sepia = sepia.Value,
            Sharpness = sharpness.Value,
            Invert = invert.IsChecked == true,
            Color = color.IsChecked == true,
            Edges = edges.IsChecked == true,
            Threshold = threshold.IsChecked == true,
            ThresholdValue = (int)(double.IsFinite(thresholdValue.Value) ? thresholdValue.Value : 128),
            Dither = (DitherMode)Math.Max(0, dither.SelectedIndex),
            Style = (ImageArtStyle)Math.Max(0, artStyle.SelectedIndex),
            MeasureGlyphDensity = measuredDensity.IsChecked == true,
            AdaptiveStrength = adaptive.Value,
            StructureThreshold = structureThreshold.Value,
            PreserveTransparent = preserveAlpha.IsChecked == true,
            TrimTransparent = trimAlpha.IsChecked == true,
            AlphaThreshold = ReadDimension(alphaThreshold, "透明阈值"),
            Background = ImagePalettes.ParseColor(background.Text),
            PaletteMode = (ImagePaletteMode)Math.Max(0, palette.SelectedIndex),
            PaletteColors = paletteColors.Text,
            PaletteSize = ReadDimension(paletteSize, "调色板数量"),
            QuickPreview = quickPreview.IsChecked == true
        };
    }
    private void Queue() { if (!suspend && IsLoaded) result.InputChanged(); if (!suspend && IsLoaded && controller.Source.Source is not null && WorkspaceService.Settings.AutoConvert) _ = App.Window.Guard(() => ConvertAsync(true)); }
    private void CancelConversion() { controller.Operations.Cancel(); processingStatus.Text = "已取消，保留当前结果"; }
    private void RefreshRegion() { if (!suspend && IsLoaded && controller.Source.Source is not null) _ = App.Window.Guard(RefreshThumbnail); }
    private void UpdateModeControls()
    {
        ramp.IsEnabled = characters.IsEnabled = measuredDensity.IsEnabled = artStyle.SelectedIndex == 0;
        structureThreshold.IsEnabled = artStyle.SelectedIndex == 1;
        var binary = artStyle.SelectedIndex == 2 || artStyle.SelectedIndex == 3 && color.IsChecked != true;
        dither.IsEnabled = artStyle.SelectedIndex == 0 || binary;
        threshold.IsEnabled = artStyle.SelectedIndex == 0;
        thresholdValue.IsEnabled = artStyle.SelectedIndex == 0 || binary;
    }
    private static int ReadDimension(NumberBox input, string name)
    {
        if (!double.IsFinite(input.Value) || input.Value != Math.Truncate(input.Value)) throw new ArgumentException($"{name}必须是整数。");
        return checked((int)input.Value);
    }
    private async Task ConvertAsync(bool automatic = false)
    {
        using var operation = controller.Operations.Begin(); var token = operation.Token;
        result.ClearTransientPreview();
        try
        {
            progress.IsActive = true; progress.Visibility = Visibility.Visible;
            cancel.IsEnabled = true; cancel.Visibility = Visibility.Visible; processingStatus.Text = "等待转换…";
            if (automatic) await Task.Delay(WorkspaceService.Settings.ConversionDelay, token);
            var family = result.CharacterFontFamily; var metrics = FontCatalog.Measure(family);
            var request = new ImageConversionRequest(controller.Source.Decoded.pixels, controller.Source.Decoded.width, controller.Source.Decoded.height, controller.Source.Revision,
                geometry.Current, Options(), family, metrics.Width, metrics.Height, resolution.SelectedIndex == 4,
                automatic, !result.HasManualEdits, controller.Source.Title);
            var converted = await controller.Convert(request, result.ShowTransientPreview,
                message => { if (operation.IsCurrent) processingStatus.Text = message; }, token);
            if (!operation.IsCurrent) return;
            lastOptions = converted.Options; result.ClearTransientPreview();
            await result.SetDocument(converted.Document, $"{converted.ElapsedMilliseconds} ms", cancellationToken: token);
            if (!operation.IsCurrent) return;
            processingStatus.Text = $"{converted.ElapsedMilliseconds} ms · {converted.Document.Width} × {converted.Document.Height} 字符";
            cacheStatus.Text = $"缓存 {pipeline.RetainedBytes / 1048576d:0.0} MB · 累计复用 {pipeline.Hits} 个阶段";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ArgumentException ex) when (automatic && operation.IsCurrent) { processingStatus.Text = ex.Message; }
        finally
        {
            // A superseded operation must not clear a newer operation's progress or preview.
            if (operation.IsCurrent || token.IsCancellationRequested && !HasNewerOperation(operation))
            { result.ClearTransientPreview(); progress.IsActive = false; progress.Visibility = Visibility.Collapsed; cancel.IsEnabled = false; cancel.Visibility = Visibility.Collapsed; }
        }
    }
    private bool HasNewerOperation(LatestOperation.Lease operation) => controller.Operations.HasNewer(operation);
    private async Task PickImage()
    {
        var picker = new FileOpenPicker(); foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }) picker.FileTypeFilter.Add(ext); App.Window.InitializePicker(picker);
        var file = await picker.PickSingleFileAsync(); if (file is not null) await LoadFile(file);
    }
    private async Task LoadFile(StorageFile file)
    {
        using var loading = controller.Loads.Begin(); controller.Operations.Cancel();
        controller.Source.LoadVersion++; controller.Source.ThumbnailVersion++;
        var bytes = await BoundedFile.ReadAsync(file.Path, 40_000_000, loading.Token);
        if (loading.IsCurrent) await LoadBytes(bytes, Path.GetFileNameWithoutExtension(file.Name));
    }
    private async Task LoadBytes(byte[] bytes, string name)
    {
        if (bytes.Length > 40_000_000) throw new InvalidDataException("输入文件超过 40MB，请先压缩图片。");
        using var loading = controller.Loads.Begin();
        controller.Operations.Cancel(); controller.Source.ThumbnailVersion++; var version = ++controller.Source.LoadVersion;
        var prepared = await controller.PrepareSource(bytes, name, token: loading.Token);
        if (version != controller.Source.LoadVersion || !loading.IsCurrent) return;
        controller.Source.Apply(prepared);
        geometry.Load(); geometry.SourceAvailable = true;
        await RefreshThumbnail(); if (version != controller.Source.LoadVersion) return;
        await ConvertAsync();
    }
    private async Task RefreshThumbnail()
    {
        if (controller.Source.Source is null) return;
        var version = ++controller.Source.ThumbnailVersion; var pixels = controller.Source.Decoded; var transform = geometry.Current; var name = controller.Source.Title;
        var revision = controller.Source.Revision; var trim = trimAlpha.IsChecked == true; var alpha = ReadDimension(alphaThreshold, "透明阈值");
        (byte[] bytes, byte[] Comparison, int Width, int Height) preview;
        await controller.Source.ThumbnailGate.WaitAsync();
        try
        {
            if (version != controller.Source.ThumbnailVersion) return;
            var prepared = await controller.Preview(new(pixels.pixels, pixels.width, pixels.height), revision, transform, trim, alpha);
            preview = (prepared.Bytes, prepared.Comparison, prepared.Width, prepared.Height);
        }
        finally { controller.Source.ThumbnailGate.Release(); }
        if (version != controller.Source.ThumbnailVersion) return;
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(preview.bytes); await writer.StoreAsync(); }
        stream.Seek(0);
        var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
        if (version != controller.Source.ThumbnailVersion) return;
        thumbnail.Source = bitmap; sourceInfo.Text = $"{name} · {preview.Width} × {preview.Height} px";
        await result.SetComparisonSources(controller.Source.OriginalPreview, preview.Comparison);
    }
    private async Task LoadBitmapReference(RandomAccessStreamReference reference)
    {
        using var loading = controller.Loads.Begin(); controller.Operations.Cancel();
        controller.Source.LoadVersion++; controller.Source.ThumbnailVersion++;
        using var stream = await reference.OpenReadAsync(); if (stream.Size > 40_000_000) throw new InvalidDataException("剪贴板图片过大。");
        loading.Token.ThrowIfCancellationRequested();
        using var reader = new DataReader(stream); await reader.LoadAsync((uint)stream.Size); var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
        if (loading.IsCurrent) await LoadBytes(bytes, "Clipboard");
    }
    private async Task Paste()
    {
        var data = Clipboard.GetContent(); if (data.Contains(StandardDataFormats.Bitmap)) await LoadBitmapReference(await data.GetBitmapAsync());
        else if (data.Contains(StandardDataFormats.StorageItems) && (await data.GetStorageItemsAsync()).FirstOrDefault() is StorageFile file) await LoadFile(file);
        else App.Window.Message("剪贴板中没有可导入的图片。");
    }
    private void Apply(ConversionOptions o)
    {
        ImageQualityConverter.Validate(o);
        suspend = true; resolution.SelectedIndex = 5; columns.Value = o.Columns; autoRows.IsChecked = o.Rows == 0; rows.Value = o.Rows == 0 ? 60 : o.Rows; cellAspect.Value = o.CellAspect; characters.Text = o.Characters; brightness.Value = o.Brightness; contrast.Value = o.Contrast; gamma.Value = o.Gamma; saturation.Value = o.Saturation; hue.Value = o.Hue; gray.Value = o.Grayscale; sepia.Value = o.Sepia; sharpness.Value = o.Sharpness; invert.IsChecked = o.Invert; color.IsChecked = o.Color; edges.IsChecked = o.Edges; threshold.IsChecked = o.Threshold; thresholdValue.Value = o.ThresholdValue; dither.SelectedIndex = (int)o.Dither; ramp.SelectedIndex = 14;
        artStyle.SelectedIndex = (int)o.Style; measuredDensity.IsChecked = o.MeasureGlyphDensity; adaptive.Value = o.AdaptiveStrength; structureThreshold.Value = o.StructureThreshold;
        preserveAlpha.IsChecked = o.PreserveTransparent; trimAlpha.IsChecked = o.TrimTransparent; alphaThreshold.Value = o.AlphaThreshold; background.Text = $"#{o.Background & 0xFFFFFF:X6}";
        palette.SelectedIndex = (int)o.PaletteMode; paletteColors.Text = o.PaletteColors; paletteSize.Value = o.PaletteSize; quickPreview.IsChecked = o.QuickPreview; suspend = false;
    }
    private void Reset()
    {
        Apply(new() { MeasureGlyphDensity = true }); geometry.Load();
        _ = App.Window.Guard(async () => { await RefreshThumbnail(); Queue(); });
    }
    public async Task LoadProject(StudioProject project)
    {
        using var loading = controller.Loads.Begin();
        controller.Operations.Cancel(); controller.Source.ThumbnailVersion++; var version = ++controller.Source.LoadVersion;
        var restored = await controller.RestoreProject(project, loading.Token);
        if (version != controller.Source.LoadVersion || !loading.IsCurrent) return;
        suspend = true;
        try
        {
            lastOptions = restored.Options; Apply(lastOptions); suspend = true;
            fontAspect.IsChecked = restored.FontAspect; resolution.SelectedIndex = restored.Resolution;
            geometry.Load(restored.Geometry); geometry.SourceAvailable = restored.Source is not null;
            if (restored.Source is { } source) controller.Source.Apply(source);
            else { controller.Source.Source = null; controller.Source.Encoded = null; controller.Source.OriginalPreview = null; controller.Source.Revision = ""; controller.Source.Decoded = default; controller.Source.Title = project.Document.Title; }
            if (restored.Source is not null) await RefreshThumbnail();
            else { thumbnail.Source = null; sourceInfo.Text = "PNG · JPEG · BMP · GIF · TIFF"; await result.SetComparisonSources(null, null); }
            if (version == controller.Source.LoadVersion) await result.LoadDocument(project);
        }
        finally { suspend = false; }
    }
}
