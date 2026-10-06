using Charloom.Core;
using Charloom.Services;
using Xunit;

namespace Charloom.Creation.Tests;

[Collection("Workspace")]
public sealed class CreationControllerTests
{
    [Fact]
    public async Task AnimatedGifUsesFirstFrameAndKeepsOriginalThroughProjectRestore()
    {
        // Two 1x1 frames: black, then white; both use the global palette.
        byte[] gif = [71,73,70,56,57,97,1,0,1,0,128,0,0,0,0,0,255,255,255,
            33,249,4,0,10,0,0,0,44,0,0,0,0,1,0,1,0,0,2,2,68,1,0,
            33,249,4,0,10,0,0,0,44,0,0,0,0,1,0,1,0,0,2,2,76,1,0,59];
        using (var stream = new MemoryStream(gif))
        using (var animation = System.Drawing.Image.FromStream(stream))
            Assert.Equal(2, animation.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time));
        var controller = new ImageCreationController();
        var prepared = await controller.PrepareSource(gif, "animated.gif");
        Assert.Equal((1, 1), (prepared.Frame.Width, prepared.Frame.Height));
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, prepared.Frame.Pixels);
        var document = ImageConverter.Convert(prepared.Frame.Pixels, 1, 1, new() { Columns = 8, Rows = 1, Color = true });
        Assert.All(document.Colors!, color => Assert.Equal(0xFF000000u, color));
        var project = controller.Project(document, new(), prepared.Encoded, true, 0, new());
        var restored = new ImageCreationController();
        var state = await restored.RestoreProject(project, default);
        restored.Source.Apply(state.Source!);
        Assert.Equal(gif, restored.Source.Source);
        Assert.Equal(prepared.Frame.Pixels, restored.Source.Decoded.pixels);
    }

    [Fact]
    public void DecodeRetainsDetailBeyondOldProcessingSide()
    {
        using var bitmap = new System.Drawing.Bitmap(3000, 1);
        using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        var decoded = ImagingService.Decode(stream.ToArray());
        Assert.Equal((3000, 1), (decoded.width, decoded.height));
        Assert.Equal(12000, decoded.pixels.Length);
    }

    [Fact]
    public async Task SourcePreparationAndSnapshotRetainOriginalBytes()
    {
        var bytes = ImagingService.Thumbnail([25, 125, 225, 255], 1, 1);
        var controller = new ImageCreationController();
        var prepared = await controller.PrepareSource(bytes, "source");
        controller.Source.Apply(prepared);
        Assert.Same(bytes, controller.Source.Source);
        Assert.Equal(bytes, System.Convert.FromBase64String(controller.Source.Encoded!));
        Assert.Equal("source", controller.Source.Title);
        Assert.Equal(1, controller.Source.Decoded.width);
        var project = controller.Project(AsciiDocument.FromText("art"), new(), prepared.Encoded, true, 4, new());
        Assert.Equal(WorkspaceService.CurrentProjectVersion, project.Version);
        Assert.Equal("True", project.Parameters!["fontAspect"]);
        Assert.Equal("4", project.Parameters["resolution"]);
    }

    [Fact]
    public async Task PreviewThenFullConversionKeepSeparateSizes()
    {
        var controller = new ImageCreationController(); var previews = new List<AsciiDocument>();
        var request = new ImageConversionRequest([0, 0, 0, 255], 1, 1, "black", new(),
            new() { Columns = 240, QuickPreview = true }, "Consolas", 6, 12, false, true, true, "test");
        var converted = await controller.Convert(request, (doc, _) => { previews.Add(doc); return Task.CompletedTask; }, _ => { }, default);
        Assert.Single(previews); Assert.Equal(120, previews[0].Width); Assert.Equal(240, converted.Document.Width);
        Assert.Equal(240, converted.Options.Columns); Assert.Equal("test", converted.Document.Title);
        Assert.Equal(6, converted.Document.CellWidth); Assert.Equal(12, converted.Document.CellHeight);
    }

    [Fact]
    public async Task CancelDuringPreviewReleasesGateForNextConversion()
    {
        var controller = new ImageCreationController(); using var cancellation = new CancellationTokenSource();
        var request = new ImageConversionRequest([0, 0, 0, 255], 1, 1, "cancel", new(),
            new() { Columns = 240, QuickPreview = true }, "Consolas", 6, 12, false, true, true, "cancel");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.Convert(request,
            (_, _) => { cancellation.Cancel(); return Task.CompletedTask; }, _ => { }, cancellation.Token));
        var converted = await controller.Convert(request with { Automatic = false }, (_, _) => Task.CompletedTask, _ => { }, default);
        Assert.Equal(240, converted.Document.Width);
    }

    [Fact]
    public void LegacyTextLayoutStillMapsWithoutModernJson()
    {
        var layout = TextProjectMapper.Layout(new() { ["border"] = "2", ["trim"] = "False", ["replacement"] = "." });
        Assert.Equal(2, layout.Border); Assert.False(layout.Trim); Assert.Equal(".", layout.Replacement);
    }

    [Fact]
    public async Task TextGenerationAndSavedMetadataAreIndependentOfLiveParameters()
    {
        var controller = new TextCreationController(); var parameters = new Dictionary<string, string> { ["font"] = "builtin:standard" };
        var request = new TextCreationRequest("ABC", 0, "builtin:standard", new(), new("Consolas"), "Consolas", 6, 12, parameters);
        var doc = await controller.Generate(request, default); controller.Accept(request);
        parameters["font"] = "changed";
        var project = controller.Project(doc);
        Assert.Equal("ABC", project.SourceText); Assert.Equal("ABC", doc.Title);
        Assert.Equal("builtin:standard", project.Parameters!["font"]);
        project.Parameters["font"] = "also changed";
        Assert.Equal("builtin:standard", controller.Project(doc).Parameters!["font"]);
    }

    [Fact]
    public void RestoringProjectCancelsGenerationAndPreview()
    {
        var controller = new TextCreationController(); using var generation = controller.Operations.Begin();
        using var preview = controller.Previews.Begin();
        controller.Restore(new(1, AsciiDocument.FromText("saved"), new() { Columns = 80 }, null, "source", "text", new() { ["font"] = "Standard" }));
        Assert.True(generation.Token.IsCancellationRequested); Assert.True(preview.Token.IsCancellationRequested);
        Assert.Equal("source", controller.GeneratedSource); Assert.Equal(80, controller.GeneratedColumns);
    }
}
