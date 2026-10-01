using System.Diagnostics;
using AsciiStudio.Controls;
using AsciiStudio.Core;
using AsciiStudio.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace AsciiStudio.Pages;

public sealed class ImagePage : Grid
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
    private readonly SemaphoreSlim conversionGate = new(1, 1);
    private readonly object transformGate = new();
    private byte[]? cachedSource;
    private ImageGeometry? cachedGeometry;
    private (byte[] Pixels, int Width, int Height) cachedTransform;
    private byte[]? source;
    private string? encodedSource;
    private ConversionOptions lastOptions = new();
    private int loadVersion;
    private int thumbnailVersion;
    private readonly SemaphoreSlim thumbnailGate = new(1, 1);
    private (byte[] pixels, int width, int height) decoded;
    private string title = "Image";
    private CancellationTokenSource? pending;
    private bool suspend;
    public ImagePage()
    {
        result.RestoreProject = LoadProject;
        var input = Ui.Stack();
        var import = Ui.Stack(10); import.Children.Add(thumbnail); import.Children.Add(sourceInfo);
        import.Children.Add(Ui.AsyncButton("选择图片 / 拖放到此处", PickImage, true));
        import.Children.Add(Ui.AsyncButton("从剪贴板粘贴", Paste)); input.Children.Add(Ui.Card(import));
        input.Children.Add(Ui.Field("输出分辨率预设", resolution));
        input.Children.Add(Ui.AsyncButton("转换", () => source is null ? throw new InvalidOperationException("请先选择图片。") : ConvertAsync(), true));
        input.Children.Add(progress);
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
        result.AddSettings("字符", characterSettings, "ImageCharacterSettings");
        var adjustments = Ui.Stack();
        adjustments.Children.Add(Ui.SettingsGrid(Ui.Field("亮度", brightness), Ui.Field("对比度", contrast), Ui.Field("Gamma", gamma), Ui.Field("饱和度", saturation), Ui.Field("色相", hue), Ui.Field("灰度", gray), Ui.Field("棕褐色", sepia), Ui.Field("锐化", sharpness), Ui.WithHelp(color, "保留原图颜色"), Ui.WithHelp(invert, "反转亮暗")));
        result.AddSettings("画面", adjustments, "ImageAdjustmentSettings");
        var effects = Ui.Stack(); effects.Width = 300;
        effects.Children.Add(Ui.WithHelp(edges, "边缘检测")); effects.Children.Add(Ui.WithHelp(threshold, "二值化")); effects.Children.Add(Ui.Field("阈值", thresholdValue));
        var reset = Ui.Button("重置全部图片参数", Reset); Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(reset, "ImageReset"); effects.Children.Add(reset);
        result.AddSettings("效果", effects, "ImageEffectSettings");
        result.AddSettings("裁剪与方向", geometry, "ImageGeometrySettings");
        result.CharacterFontChanged += _ => { if (!suspend) Queue(); };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(fontAspect, "ImageFontAspect");
        fontAspect.Checked += (_, _) => { cellAspect.IsEnabled = false; Queue(); };
        fontAspect.Unchecked += (_, _) => { cellAspect.IsEnabled = autoRows.IsChecked == true; Queue(); };
        cellAspect.IsEnabled = false;
        geometry.Changed += async () => await App.Window.Guard(async () =>
        {
            pending?.Cancel();
            await RefreshThumbnail(); if (IsLoaded) Queue();
        });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(sourceInfo, "ImageSourceInfo");
        Unloaded += (_, _) => { pending?.Cancel(); thumbnailVersion++; };
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
        result.ProjectFactory = doc => new(1, doc, lastOptions, encodedSource, null, "image", new() { ["fontAspect"] = (fontAspect.IsChecked == true).ToString(), ["resolution"] = resolution.SelectedIndex.ToString() }, Geometry: geometry.Current);
        suspend = true; columns.Value = WorkspaceService.Settings.DefaultColumns; resolution.SelectedIndex = columns.Value == 120 ? 0 : 5; suspend = false;
    }

    private ConversionOptions Options()
    {
        var metrics = FontCatalog.Measure(result.CharacterFontFamily);
        var aspect = fontAspect.IsChecked == true ? metrics.Width / metrics.Height : cellAspect.Value;
        var count = ReadDimension(columns, "列数");
        if (resolution.SelectedIndex == 4 && source is not null)
        {
            var crop = geometry.Current; var w = decoded.width * crop.Width / 100; var h = decoded.height * crop.Height / 100;
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
            Dither = (DitherMode)Math.Max(0, dither.SelectedIndex)
        };
    }
    private void Queue() { if (!suspend && source is not null && WorkspaceService.Settings.AutoConvert) _ = App.Window.Guard(ConvertAsync); }
    private static int ReadDimension(NumberBox input, string name)
    {
        if (!double.IsFinite(input.Value) || input.Value != Math.Truncate(input.Value)) throw new ArgumentException($"{name}必须是整数。");
        return checked((int)input.Value);
    }
    private async Task ConvertAsync()
    {
        pending?.Cancel(); var tokenSource = new CancellationTokenSource(); pending = tokenSource; var token = tokenSource.Token;
        try
        {
            await Task.Delay(WorkspaceService.Settings.ConversionDelay, token); var options = Options(); var pixels = decoded;
            var transform = geometry.Current;
            var family = result.CharacterFontFamily;
            var metrics = FontCatalog.Measure(family);
            await conversionGate.WaitAsync(token);
            progress.IsActive = true; var sw = Stopwatch.StartNew();
            AsciiDocument doc;
            try
            {
                doc = await Task.Run(() =>
            {
                var transformed = Transformed(pixels, transform, token);
                return ImageConverter.Convert(transformed.Pixels, transformed.Width, transformed.Height, options, token) with { FontFamily = family, CellWidth = metrics.Width, CellHeight = metrics.Height };
            }, token);
            }
            finally { conversionGate.Release(); }
            if (!token.IsCancellationRequested && ReferenceEquals(pending, tokenSource)) { lastOptions = options; await result.SetDocument(doc with { Title = title }, $"{sw.ElapsedMilliseconds} ms"); }
        }
        finally { if (ReferenceEquals(pending, tokenSource)) { progress.IsActive = false; pending = null; } tokenSource.Dispose(); }
    }
    private (byte[] Pixels, int Width, int Height) Transformed((byte[] pixels, int width, int height) pixels, ImageGeometry transform, CancellationToken token = default)
    {
        lock (transformGate)
        {
            token.ThrowIfCancellationRequested();
            if (ReferenceEquals(cachedSource, pixels.pixels) && cachedGeometry == transform) return cachedTransform;
            var output = transform == new ImageGeometry() ? (pixels.pixels, pixels.width, pixels.height)
                : ImageTransforms.Apply(pixels.pixels, pixels.width, pixels.height, transform, token);
            cachedSource = pixels.pixels; cachedGeometry = transform; cachedTransform = output; return output;
        }
    }
    private async Task PickImage()
    {
        var picker = new FileOpenPicker(); foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }) picker.FileTypeFilter.Add(ext); App.Window.InitializePicker(picker);
        var file = await picker.PickSingleFileAsync(); if (file is not null) await LoadFile(file);
    }
    private async Task LoadFile(StorageFile file)
    {
        if (new FileInfo(file.Path).Length > 40_000_000) throw new InvalidDataException("输入文件超过 40MB，请先压缩图片。");
        await LoadBytes(await File.ReadAllBytesAsync(file.Path), Path.GetFileNameWithoutExtension(file.Name));
    }
    private async Task LoadBytes(byte[] bytes, string name)
    {
        if (bytes.Length > 40_000_000) throw new InvalidDataException("输入文件超过 40MB，请先压缩图片。");
        pending?.Cancel(); thumbnailVersion++; var version = ++loadVersion;
        var image = await Task.Run(() => ImagingService.Decode(bytes)); var encoded = await Task.Run(() => Convert.ToBase64String(bytes));
        if (version != loadVersion) return; source = bytes; encodedSource = encoded; decoded = image; title = name;
        geometry.Load(); geometry.SourceAvailable = true;
        await RefreshThumbnail(); if (version != loadVersion) return;
        await ConvertAsync();
    }
    private async Task RefreshThumbnail()
    {
        if (source is null) return;
        var version = ++thumbnailVersion; var pixels = decoded; var transform = geometry.Current; var name = title;
        (byte[] bytes, int Width, int Height) preview;
        await thumbnailGate.WaitAsync();
        try
        {
            if (version != thumbnailVersion) return;
            preview = await Task.Run(() =>
            {
                var image = Transformed(pixels, transform);
                return (bytes: ImagingService.Thumbnail(image.Pixels, image.Width, image.Height), image.Width, image.Height);
            });
        }
        finally { thumbnailGate.Release(); }
        if (version != thumbnailVersion) return;
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(preview.bytes); await writer.StoreAsync(); }
        stream.Seek(0);
        var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
        if (version != thumbnailVersion) return;
        thumbnail.Source = bitmap; sourceInfo.Text = $"{name} · {preview.Width} × {preview.Height} px";
    }
    private async Task LoadBitmapReference(RandomAccessStreamReference reference)
    {
        using var stream = await reference.OpenReadAsync(); if (stream.Size > 40_000_000) throw new InvalidDataException("剪贴板图片过大。");
        using var reader = new DataReader(stream); await reader.LoadAsync((uint)stream.Size); var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes); await LoadBytes(bytes, "Clipboard");
    }
    private async Task Paste()
    {
        var data = Clipboard.GetContent(); if (data.Contains(StandardDataFormats.Bitmap)) await LoadBitmapReference(await data.GetBitmapAsync());
        else if (data.Contains(StandardDataFormats.StorageItems) && (await data.GetStorageItemsAsync()).FirstOrDefault() is StorageFile file) await LoadFile(file);
        else App.Window.Message("剪贴板中没有可导入的图片。");
    }
    private void Apply(ConversionOptions o)
    {
        suspend = true; resolution.SelectedIndex = 5; columns.Value = o.Columns; autoRows.IsChecked = o.Rows == 0; rows.Value = o.Rows == 0 ? 60 : o.Rows; cellAspect.Value = o.CellAspect; characters.Text = o.Characters; brightness.Value = o.Brightness; contrast.Value = o.Contrast; gamma.Value = o.Gamma; saturation.Value = o.Saturation; hue.Value = o.Hue; gray.Value = o.Grayscale; sepia.Value = o.Sepia; sharpness.Value = o.Sharpness; invert.IsChecked = o.Invert; color.IsChecked = o.Color; edges.IsChecked = o.Edges; threshold.IsChecked = o.Threshold; thresholdValue.Value = o.ThresholdValue; dither.SelectedIndex = (int)o.Dither; ramp.SelectedIndex = 14; suspend = false;
    }
    private void Reset()
    {
        Apply(new()); geometry.Load();
        _ = App.Window.Guard(async () => { await RefreshThumbnail(); Queue(); });
    }
    public async Task LoadProject(StudioProject project)
    {
        pending?.Cancel(); thumbnailVersion++; var version = ++loadVersion;
        byte[]? bytes = null;
        (byte[] pixels, int width, int height) image = default;
        if (project.SourceImage is not null)
        {
            bytes = Convert.FromBase64String(project.SourceImage);
            if (bytes.Length > 40_000_000) throw new InvalidDataException("项目中的图片超过 40MB。");
            image = await Task.Run(() => ImagingService.Decode(bytes));
        }
        if (version != loadVersion) return;
        suspend = true;
        try
        {
            lastOptions = project.Options ?? new(); Apply(lastOptions); suspend = true;
            fontAspect.IsChecked = project.Parameters is { } parameters && parameters.TryGetValue("fontAspect", out var automatic) && bool.TryParse(automatic, out var enabled) && enabled;
            if (project.Parameters is { } presetParameters && presetParameters.TryGetValue("resolution", out var preset) && int.TryParse(preset, out var pi) && pi is >= 0 and <= 5) resolution.SelectedIndex = pi;
            geometry.Load(project.Geometry); geometry.SourceAvailable = bytes is not null;
            source = bytes; encodedSource = project.SourceImage; decoded = image; title = project.Document.Title;
            if (bytes is not null) await RefreshThumbnail();
            else { thumbnail.Source = null; sourceInfo.Text = "PNG · JPEG · BMP · GIF · TIFF"; }
            if (version == loadVersion) await result.LoadDocument(project);
        }
        finally { suspend = false; }
    }
}
