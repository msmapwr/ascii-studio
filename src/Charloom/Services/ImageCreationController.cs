using System.Diagnostics;
using Charloom.Core;

namespace Charloom.Services;

public sealed record ImageConversionRequest(byte[] Pixels, int Width, int Height, string Revision,
    ImageGeometry Geometry, ConversionOptions Options, string Family, double CellWidth,
    double CellHeight, bool NativeSize, bool Automatic, bool Preview, string Title);
public sealed record ImageConversionResult(AsciiDocument Document, ConversionOptions Options, long ElapsedMilliseconds);
public sealed record ImageProjectState(ImageSourceSnapshot? Source, ConversionOptions Options, bool FontAspect, int Resolution, ImageGeometry Geometry);

/// <summary>Coordinates CPU conversion and interim previews without depending on WinUI controls.</summary>
public sealed class ImageCreationController
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public LatestOperation Operations { get; } = new();
    public LatestOperation Loads { get; } = new();
    public ImagePipelineService Pipeline { get; } = new();
    public ImageSourceState Source { get; } = new();
    public ConversionOptions LastOptions { get; set; } = new() { MeasureGlyphDensity = true };

    public Task<ImageSourceSnapshot> PrepareSource(byte[] bytes, string title, string? encoded = null, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (bytes.Length > ImageResourceLimits.FileBytes) throw new InvalidDataException("输入文件超过 100MB，请先压缩图片。");
        var decoded = Pipeline.Decode(bytes);
        token.ThrowIfCancellationRequested();
        return new ImageSourceSnapshot(bytes, decoded.Key, encoded ?? System.Convert.ToBase64String(bytes), decoded.Frame,
            ImagingService.Thumbnail(decoded.Frame.Pixels, decoded.Frame.Width, decoded.Frame.Height, 2400), title);
    }, token);

    public async Task<ImageProjectState> RestoreProject(StudioProject project, CancellationToken token)
    {
        var options = project.Options ?? new(); ImageQualityConverter.Validate(options);
        var geometry = project.Geometry ?? new(); geometry.Validate();
        ImageSourceSnapshot? source = null;
        if (project.SourceImage is { } encoded)
        {
            if (encoded.Length > ((long)ImageResourceLimits.FileBytes + 2) / 3 * 4) throw new InvalidDataException("项目中的图片超过 100MB。");
            var bytes = System.Convert.FromBase64String(encoded);
            source = await PrepareSource(bytes, project.Document.Title, encoded, token);
        }
        var p = project.Parameters;
        var fontAspect = bool.TryParse(p?.GetValueOrDefault("fontAspect"), out var automatic) && automatic;
        var resolution = int.TryParse(p?.GetValueOrDefault("resolution"), out var preset) && preset is >= 0 and <= 5 ? preset : 5;
        token.ThrowIfCancellationRequested();
        return new(source, options, fontAspect, resolution, geometry);
    }

    public async Task<ImageConversionResult> Convert(ImageConversionRequest request,
        Func<AsciiDocument, CancellationToken, Task> preview, Action<string> status, CancellationToken token)
    {
        var options = request.Options;
        ImageQualityConverter.Validate(options);
        await gate.WaitAsync(token);
        try
        {
            var timer = Stopwatch.StartNew();
            if (request.NativeSize)
            {
                var frame = await Task.Run(() => Pipeline.Transform(request.Pixels, request.Width, request.Height,
                    request.Revision, request.Geometry, options.TrimTransparent, options.AlphaThreshold, token), token);
                var count = Math.Clamp((int)Math.Round(frame.Width / request.CellWidth), 8, ImageResourceLimits.GridSide);
                count = Math.Max(8, Math.Min(count, (int)Math.Floor((double)ImageResourceLimits.GridSide * frame.Width / frame.Height / options.CellAspect)));
                options = options with { Columns = count, Rows = 0 };
            }
            async Task<AsciiDocument> Render(ConversionOptions settings) => await Task.Run(() =>
                Pipeline.Convert(request.Pixels, request.Width, request.Height, request.Revision, request.Geometry,
                    settings, request.Family, token) with
                {
                    FontFamily = request.Family,
                    CellWidth = request.CellWidth,
                    CellHeight = request.CellHeight,
                    Title = request.Title
                }, token);
            if (request.Automatic && options.QuickPreview && options.Columns > 120 && request.Preview)
            {
                var low = options with { Columns = 120, Rows = options.Rows == 0 ? 0 : Math.Max(1, (int)Math.Round(options.Rows * 120d / options.Columns)) };
                var document = await Render(low);
                token.ThrowIfCancellationRequested();
                await preview(document, token);
                status("低成本预览 · 正在准备完整结果");
                await Task.Delay(300, token);
            }
            token.ThrowIfCancellationRequested();
            status("正在转换…");
            var result = await Render(options);
            token.ThrowIfCancellationRequested();
            return new(result, options, timer.ElapsedMilliseconds);
        }
        finally { gate.Release(); }
    }

    public Task<(byte[] Bytes, byte[] Comparison, int Width, int Height)> Preview(ImageFrame frame,
        string revision, ImageGeometry geometry, bool trim, int alpha) => Task.Run(() =>
    {
        var image = Pipeline.Transform(frame.Pixels, frame.Width, frame.Height, revision, geometry, trim, alpha);
        return (Pipeline.Thumbnail(image, revision, geometry, trim, alpha, 300),
            Pipeline.Thumbnail(image, revision, geometry, trim, alpha, 2400), image.Width, image.Height);
    });

    public StudioProject Project(AsciiDocument document, ConversionOptions options, string? source,
        bool fontAspect, int resolution, ImageGeometry geometry) => new(WorkspaceService.CurrentProjectVersion,
        document, options, source, null, "image", new()
        {
            ["fontAspect"] = fontAspect.ToString(),
            ["resolution"] = resolution.ToString()
        }, Geometry: geometry);
}
