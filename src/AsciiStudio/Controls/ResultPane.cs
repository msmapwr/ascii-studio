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
    private readonly ComboBox exportScale=Ui.Choice(["1×","2×","3×","4×"]);
    private readonly TextBlock exportDimensions=Ui.Text("生成后显示图片分辨率",12,true);
    private bool updating;
    private int renderVersion;
    private readonly DispatcherTimer recoveryTimer = new() { Interval=TimeSpan.FromMilliseconds(800) };
    public AsciiDocument? Document { get; private set; }
    public Func<AsciiDocument, StudioProject>? ProjectFactory { get; set; }

    public ResultPane()
    {
        RowSpacing = 12;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Ui.AsyncButton("复制", () => { if (Document is not null) { var package = new DataPackage(); package.SetText(Document.Text); Clipboard.SetContent(package); App.Window.Message("已复制到剪贴板"); } return Task.CompletedTask; }));
        actions.Children.Add(Ui.AsyncButton("保存项目", SaveProject));
        format.Width = 118; actions.Children.Add(format); actions.Children.Add(Ui.AsyncButton("导出", Export, true));
        var toolbar=Ui.Stack(8);toolbar.Children.Add(actions);
        var resolutionRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};resolutionRow.Children.Add(Ui.Text("图片导出倍率",12,true));exportScale.Width=80;resolutionRow.Children.Add(exportScale);resolutionRow.Children.Add(exportDimensions);toolbar.Children.Add(resolutionRow);Children.Add(toolbar);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(exportScale,"ExportScale");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(exportScale,"图片导出倍率");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(exportDimensions,"ExportDimensions");
        exportScale.SelectionChanged+=(_,_)=>UpdateDimensions();
        editor = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(20), PlaceholderText = "转换结果将在这里显示。\n生成后可以直接编辑，再复制或导出。", HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(editor,"ResultEditor");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor,"字符画结果编辑器");
        editor.TextChanged += (_, _) =>
        {
            if (updating) return;
            try { Document = AsciiDocument.FromText(editor.Text, Document?.Title ?? "Untitled"); UpdateStats("已编辑 · 颜色已重置");recoveryTimer.Stop();recoveryTimer.Start(); }
            catch (ArgumentException ex) { updating=true;editor.Text=Document?.Text??"";updating=false;App.Window.Message(ex.Message, true); }
        };
        var canvas = new Grid(); canvas.Children.Add(editor);
        imageScroll = new ScrollViewer { Content = preview, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
        canvas.Children.Add(imageScroll); var card = Ui.Card(canvas, new Thickness(0)); Grid.SetRow(card, 1); Children.Add(card);
        var footer = new Grid { ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        stats = Ui.Text("尚未生成结果", 12, true); stats.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(stats);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(stats,"ResultStats");
        Grid.SetColumn(fontSize, 1); footer.Children.Add(fontSize); Grid.SetColumn(colorToggle, 2); footer.Children.Add(colorToggle);
        fontSize.ValueChanged += async (_, _) => { if (double.IsFinite(fontSize.Value)) { editor.FontSize = fontSize.Value;UpdateDimensions(); if (colorToggle.IsOn) await App.Window.Guard(RenderPreview); } };
        colorToggle.Toggled += async (_, _) => { imageScroll.Visibility = colorToggle.IsOn ? Visibility.Visible : Visibility.Collapsed; editor.Visibility = colorToggle.IsOn ? Visibility.Collapsed : Visibility.Visible; if (colorToggle.IsOn) await App.Window.Guard(RenderPreview); };
        Grid.SetRow(footer, 2); Children.Add(footer);
        recoveryTimer.Tick+=async (_,_)=>{recoveryTimer.Stop();await App.Window.Guard(SaveRecovery);};
        Unloaded+=(_,_)=>recoveryTimer.Stop();
    }

    public async Task SetDocument(AsciiDocument document, string suffix = "")
    {
        document.Validate(); Document = document; updating = true; editor.Text = document.Text; updating = false;
        UpdateStats(suffix); if (colorToggle.IsOn) await RenderPreview();
        recoveryTimer.Stop();recoveryTimer.Start();
    }
    private async Task SaveRecovery()
    {
        if(Document is null)return;
        var snapshot=ProjectFactory?.Invoke(Document)??new StudioProject(1,Document,null,null,null,"snapshot");
        var bytes=await Task.Run(()=>System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot));
        if(bytes.Length>100_000_000)throw new InvalidDataException("恢复项目超过 100MB，请降低字符画尺寸或输入图片大小。");
        await WorkspaceService.AtomicWrite(Path.Combine(WorkspaceService.DataDirectory,"recovery.asciiproj"),bytes);
    }

    private void UpdateStats(string suffix = "")
    {
        stats.Text=Document is null ? "尚未生成结果" : $"{Document.Width} × {Document.Height} · {Document.Text.Length:N0} 字符{(suffix.Length > 0 ? " · " + suffix : "")}";
        UpdateDimensions();
    }
    private void UpdateDimensions()
    {
        if(Document is null)return;
        try{var size=ImagingService.RenderSize(Document,(float)fontSize.Value,scale:exportScale.SelectedIndex+1);exportDimensions.Text=$"{size.Width} × {size.Height} px · PNG / JPEG / GIF";}
        catch(ArgumentException ex){exportDimensions.Text=ex.Message;}
    }
    private async Task RenderPreview()
    {
        if (Document is null) return;
        var current = ++renderVersion; var document = Document; var size = (float)fontSize.Value;
        var bytes = await Task.Run(() => ImagingService.Render(document, size));
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
        stream.Seek(0); var source = new BitmapImage(); await source.SetSourceAsync(stream);
        if (current == renderVersion) preview.Source = source;
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
        var picker = new FileSavePicker { SuggestedFileName = SafeName(doc.Title), SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add(kind, [ext]); App.Window.InitializePicker(picker);
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        var size = (float)fontSize.Value;var scale=exportScale.SelectedIndex+1;
        var bytes = await Task.Run(() => kind switch
        {
            "PNG" => ImagingService.Render(doc, size,scale:scale), "JPEG" => ImagingService.Render(doc, size, format: ImageFormat.Jpeg,scale:scale),
            "GIF" => ImagingService.Render(doc, size, format: ImageFormat.Gif,scale:scale),
            "HTML" => Encoding.UTF8.GetBytes(ExportService.Html(doc)), "SVG" => Encoding.UTF8.GetBytes(ExportService.Svg(doc)),
            "ANSI" => Encoding.UTF8.GetBytes(ExportService.Ansi(doc)), "JSON" => Encoding.UTF8.GetBytes(ExportService.Json(doc)),
            "Markdown" => Encoding.UTF8.GetBytes(ExportService.Markdown(doc)), _ => Encoding.UTF8.GetBytes(doc.Text.Replace("\n", "\r\n"))
        });
        await WorkspaceService.AtomicWrite(file.Path, bytes); App.Window.Message($"已导出 {kind}：{Path.GetFileName(file.Path)}");
    }
    private static string SafeName(string? title)
    {
        var name=new string((title??"Untitled").Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).Take(80).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(name)?"Untitled":name;
    }
}
