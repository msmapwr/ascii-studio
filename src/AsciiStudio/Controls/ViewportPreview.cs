using AsciiStudio.Core;
using AsciiStudio.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace AsciiStudio.Controls;

/// <summary>Only the current viewport has a raster allocation; canvas dimensions describe the full document.</summary>
public sealed class ViewportPreview : Grid
{
    private readonly Grid views = new();
    private readonly Canvas resultCanvas = new(), sourceCanvas = new();
    private readonly Image tile = new() { Stretch = Stretch.Fill }, source = new() { Stretch = Stretch.Fill };
    private readonly ScrollViewer resultScroll, sourceScroll;
    private readonly Thumb divider = new() { Width = 8, HorizontalAlignment = HorizontalAlignment.Left, Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] };
    private readonly DispatcherTimer renderTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private CancellationTokenSource? pending;
    private DocumentViewIndex? index;
    private float fontSize = 13;
    private double zoom = 1;
    private double sourceZoom = 1;
    private int mode;
    private double split = .5;
    private bool synchronize = true;
    private BitmapImage? originalSource, processedSource;
    private byte[]? originalBytes, processedBytes;
    private Task sourceLoadTask = Task.CompletedTask;
    private int sourceVersion;
    private bool preferProcessed = true;
    public ScrollViewer Scroll => mode == 1 ? sourceScroll : resultScroll;
    public ScrollViewer ScrollAt(DependencyObject? node)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, sourceScroll)) return sourceScroll;
            node = VisualTreeHelper.GetParent(node);
        }
        return Scroll;
    }
    public double ExtentWidth => resultCanvas.Width;
    public double ExtentHeight => resultCanvas.Height;
    public event Action<string>? Rendered;
    public event Action<double>? SplitChanged;
    public ViewportPreview()
    {
        resultCanvas.Children.Add(tile); sourceCanvas.Children.Add(source);
        resultScroll = CreateScroll(resultCanvas, "ResultViewportScroll");
        sourceScroll = CreateScroll(sourceCanvas, "SourceViewportScroll");
        AutomationProperties.SetAutomationId(tile, "ResultViewportTile");
        AutomationProperties.SetName(tile, "字符画可见区域预览");
        AutomationProperties.SetAutomationId(source, "ComparisonSourceImage");
        AutomationProperties.SetName(source, "用于对比的输入图像");
        AutomationProperties.SetAutomationId(divider, "ComparisonDivider");
        AutomationProperties.SetName(divider, "拖动原图对比分界线");
        views.ColumnDefinitions.Add(new()); views.ColumnDefinitions.Add(new());
        views.Children.Add(resultScroll); views.Children.Add(sourceScroll); views.Children.Add(divider);
        Children.Add(views);
        resultScroll.ViewChanged += (_, _) => { Sync(resultScroll, sourceScroll); QueueRender(); };
        sourceScroll.ViewChanged += (_, _) => Sync(sourceScroll, resultScroll);
        SizeChanged += (_, _) => { UpdateClip(); QueueRender(); };
        divider.DragDelta += (_, args) => SetSplit(split + args.HorizontalChange / Math.Max(1, views.ActualWidth));
        renderTimer.Tick += async (_, _) => { renderTimer.Stop(); await App.Window.Guard(Render); };
        Loaded += (_, _) => QueueRender();
        Unloaded += (_, _) => { renderTimer.Stop(); pending?.Cancel(); };
        SetMode(0);
    }
    private static ScrollViewer CreateScroll(Canvas content, string id)
    {
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetAutomationId(scroll, id); return scroll;
    }
    public void SetDocument(DocumentViewIndex next)
    {
        pending?.Cancel(); index = next; tile.Source = null; UpdateExtent(); QueueRender();
    }
    public void SetScale(float size, double factor)
    {
        fontSize = size; zoom = factor; if (synchronize || mode == 3) sourceZoom = factor; UpdateExtent(); QueueRender();
    }
    private void UpdateExtent()
    {
        if (index is null) return;
        var metrics = FontCatalog.Measure(index.Document.FontFamily);
        var width = index.Document.Width * metrics.Width * fontSize / 13;
        var height = index.Document.Height * metrics.Height * fontSize / 13;
        resultCanvas.Width = (width + 40) * zoom; resultCanvas.Height = (height + 40) * zoom;
        sourceCanvas.Width = (width + 40) * sourceZoom; sourceCanvas.Height = (height + 40) * sourceZoom;
        source.Width = width * sourceZoom; source.Height = height * sourceZoom; source.Stretch = Stretch.Uniform;
        Canvas.SetLeft(source, 20 * sourceZoom); Canvas.SetTop(source, 20 * sourceZoom);
    }
    public void SetMode(int value)
    {
        var nextMode = Math.Clamp(value, 0, 3); var changed = nextMode != mode;
        var previousScroll = Scroll;
        var horizontal = previousScroll.ScrollableWidth > 0 ? previousScroll.HorizontalOffset / previousScroll.ScrollableWidth : 0;
        var vertical = previousScroll.ScrollableHeight > 0 ? previousScroll.VerticalOffset / previousScroll.ScrollableHeight : 0;
        mode = nextMode;
        if (mode == 3) { sourceZoom = zoom; UpdateExtent(); }
        resultScroll.Visibility = mode == 1 ? Visibility.Collapsed : Visibility.Visible;
        sourceScroll.Visibility = mode == 0 ? Visibility.Collapsed : Visibility.Visible;
        divider.Visibility = mode == 3 ? Visibility.Visible : Visibility.Collapsed;
        views.ColumnDefinitions[0].Width = new(1, GridUnitType.Star);
        views.ColumnDefinitions[1].Width = mode == 2 ? new(1, GridUnitType.Star) : new(0);
        Grid.SetColumn(resultScroll, mode == 2 ? 1 : 0); Grid.SetColumn(sourceScroll, 0);
        if (mode > 0) _ = App.Window.Guard(LoadSources);
        UpdateClip(); QueueRender();
        if (mode == 1) Rendered?.Invoke("原图预览");
        // Hidden viewers have stale extent metrics. Reapply the active view's
        // relative position after measuring the new comparison layout.
        if (changed && (synchronize || mode == 3)) DispatcherQueue.TryEnqueue(() =>
        {
            if (mode != nextMode) return;
            views.UpdateLayout();
            resultScroll.ChangeView(horizontal * resultScroll.ScrollableWidth, vertical * resultScroll.ScrollableHeight, null, true);
            sourceScroll.ChangeView(horizontal * sourceScroll.ScrollableWidth, vertical * sourceScroll.ScrollableHeight, null, true);
            QueueRender();
        });
    }
    public void SetSync(bool enabled) { synchronize = enabled; if (enabled) { sourceZoom = zoom; UpdateExtent(); Sync(resultScroll, sourceScroll); } }
    public bool HandleIndependentSourceWheel(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (synchronize || mode == 3 || (args.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) == 0) return false;
        var node = args.OriginalSource as DependencyObject;
        while (node is not null && !ReferenceEquals(node, sourceScroll)) node = VisualTreeHelper.GetParent(node);
        if (node is null) return false;
        var point = args.GetCurrentPoint(sourceScroll); var previous = sourceZoom;
        sourceZoom = Math.Clamp(sourceZoom * Math.Pow(1.1, point.Properties.MouseWheelDelta / 120d), .25, 4);
        var x = sourceScroll.HorizontalOffset; var y = sourceScroll.VerticalOffset; UpdateExtent();
        DispatcherQueue.TryEnqueue(() => sourceScroll.ChangeView((x + point.Position.X) * sourceZoom / previous - point.Position.X, (y + point.Position.Y) * sourceZoom / previous - point.Position.Y, null, true));
        args.Handled = true; return true;
    }
    public void SetSplit(double fraction)
    {
        var next = Math.Clamp(fraction, .05, .95);
        if (Math.Abs(next - split) < .000001) return;
        split = next; UpdateClip(); SplitChanged?.Invoke(split);
    }
    private void UpdateClip()
    {
        sourceScroll.Clip = mode == 3 ? new RectangleGeometry { Rect = new Rect(0, 0, views.ActualWidth * split, views.ActualHeight) } : null;
        divider.Margin = new Thickness(Math.Max(0, views.ActualWidth * split - 4), 0, 0, 0);
    }
    private void Sync(ScrollViewer from, ScrollViewer to)
    {
        if ((!synchronize && mode != 3) || mode == 0) return;
        var x = from.ScrollableWidth > 0 ? from.HorizontalOffset / from.ScrollableWidth * to.ScrollableWidth : 0;
        var y = from.ScrollableHeight > 0 ? from.VerticalOffset / from.ScrollableHeight * to.ScrollableHeight : 0;
        if (Math.Abs(to.HorizontalOffset - x) > .5 || Math.Abs(to.VerticalOffset - y) > .5) to.ChangeView(x, y, null, true);
    }
    public async Task SetSources(byte[]? original, byte[]? processed)
    {
        sourceVersion++; originalBytes = original; processedBytes = processed;
        originalSource = processedSource = null; source.Source = null;
        sourceLoadTask = Task.CompletedTask;
        if (mode > 0) await LoadSources();
    }
    private Task LoadSources()
    {
        if (originalSource is not null || originalBytes is null) return Task.CompletedTask;
        if (!sourceLoadTask.IsCompleted) return sourceLoadTask;
        sourceLoadTask = DecodeSources(); return sourceLoadTask;
    }
    private async Task DecodeSources()
    {
        var version = sourceVersion; var original = originalBytes; var processed = processedBytes;
        async Task<BitmapImage?> Decode(byte[]? bytes)
        {
            if (bytes is null) return null;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
            stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); return bitmap;
        }
        var originalBitmap = await Decode(original); var processedBitmap = await Decode(processed);
        if (version != sourceVersion) return;
        originalSource = originalBitmap; processedSource = processedBitmap; SelectSource(preferProcessed);
    }
    public void SelectSource(bool processed) { preferProcessed = processed; source.Source = processed ? processedSource ?? originalSource : originalSource; }
    private void QueueRender()
    {
        pending?.Cancel();
        if (!IsLoaded || Visibility != Visibility.Visible) return;
        renderTimer.Stop(); renderTimer.Start();
    }
    public void Refresh() => QueueRender();
    private async Task Render()
    {
        if (index is null || mode == 1 || resultScroll.ViewportWidth < 1 || resultScroll.ViewportHeight < 1) return;
        var next = new CancellationTokenSource(); pending?.Cancel(); pending = next;
        var token = next.Token; var documentIndex = index; var size = fontSize; var scale = zoom;
        var region = ViewportRegion.Create(resultScroll.HorizontalOffset, resultScroll.VerticalOffset,
            resultScroll.ViewportWidth, resultScroll.ViewportHeight, XamlRoot?.RasterizationScale ?? 1);
        try
        {
            await renderGate.WaitAsync(token);
            byte[] bytes;
            try { bytes = await Task.Run(() => ViewportRasterizer.Render(documentIndex, size, scale, region, token), token); }
            finally { renderGate.Release(); }
            token.ThrowIfCancellationRequested();
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
            stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
            if (token.IsCancellationRequested || !ReferenceEquals(index, documentIndex)) return;
            tile.Source = bitmap; tile.Width = region.Width; tile.Height = region.Height;
            Canvas.SetLeft(tile, region.X); Canvas.SetTop(tile, region.Y);
            Rendered?.Invoke($"可见区域 {region.PixelWidth} × {region.PixelHeight} px · {(scale < .25 ? "缩小概览" : "字符预览")}");
        }
        finally { if (ReferenceEquals(pending, next)) pending = null; next.Dispose(); }
    }
}
