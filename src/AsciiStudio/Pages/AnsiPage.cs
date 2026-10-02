using System.Text;
using AsciiStudio.Controls;
using AsciiStudio.Core;
using AsciiStudio.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace AsciiStudio.Pages;

public sealed class AnsiPage : Grid, IProjectSessionPage
{
    private readonly ResultPane result = new();
    public ResultPane ResultPane => result;
    public string SessionMode => "ansi";
    public event Action<bool>? DirtyChanged { add => result.DirtyChanged += value; remove => result.DirtyChanged -= value; }
    public event Action<AsciiDocument>? DocumentChanged { add => result.DocumentChanged += value; remove => result.DocumentChanged -= value; }
    public void SetSession(string id, string? path) => result.SetSession(id, path);
    public Task<bool> SaveProjectAsync() => result.SaveProjectAsync();
    public Task SaveRecoveryAsync() => result.SaveRecoveryAsync();
    private readonly TextBox input = new() { AcceptsReturn = true, Height = 200, MaxLength = AnsiArt.InputLimit, TextWrapping = TextWrapping.NoWrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
    private readonly ComboBox encoding = Ui.Choice(["自动（UTF-8 / CP437）", "UTF-8", "CP437"]);
    private readonly NumberBox columns = new() { Value = 80, Minimum = 20, Maximum = 300, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly ToggleSwitch ice = new() { Header = "iCE 高亮背景" };
    private readonly TextBlock info = Ui.Text("打开 ANSI 文件，或粘贴包含转义序列的原文。", 12, true), metadata = Ui.Text("", 12, true);
    private readonly ProgressRing progress = new() { Width = 24, Height = 24 };
    private byte[]? sourceBytes, parsedBytes;
    private string sourceText = "", title = "ANSI art", parsedEncoding = "Auto";
    private string? parsedText;
    private int parsedColumns = 80, version;
    private bool parsedIce, applying;
    private CancellationTokenSource? pending;

    public AnsiPage()
    {
        result.RestoreProject = LoadProject;
        var panel = Ui.Stack();
        panel.Children.Add(Ui.AsyncButton("打开 ANSI 文件", Pick, true)); panel.Children.Add(Ui.AsyncButton("粘贴原文", Paste)); panel.Children.Add(Ui.AsyncButton("载入彩色示例", Sample));
        panel.Children.Add(Ui.Field("ANSI 原文", input)); AutomationProperties.SetAutomationId(input, "AnsiInput");
        var render = Ui.AsyncButton("查看", Parse, true); AutomationProperties.SetAutomationId(render, "AnsiRender"); panel.Children.Add(render);
        panel.Children.Add(info); panel.Children.Add(metadata); panel.Children.Add(progress);
        AutomationProperties.SetAutomationId(info, "AnsiStatus"); AutomationProperties.SetAutomationId(metadata, "AnsiMetadata");
        var settings = Ui.Stack(); settings.Width = 340;
        settings.Children.Add(Ui.Field("ANSI 文本编码", encoding)); settings.Children.Add(Ui.Field("ANSI 换行列数", columns)); settings.Children.Add(Ui.WithHelp(ice, "iCE 高亮背景"));
        AutomationProperties.SetAutomationId(encoding, "AnsiEncoding"); AutomationProperties.SetAutomationId(columns, "AnsiColumns"); AutomationProperties.SetAutomationId(ice, "AnsiIce");
        settings.Children.Add(Ui.Text("最多 4 MB 输入 / 2000 行。闪烁静态显示；未知指令仅统计，不执行。", 12, true));
        result.AddSettings("ANSI 设置", settings, "AnsiSettings");
        result.ProjectFactory = document => new(1, document, null, null, parsedText, "ansi", new()
        {
            ["encoding"] = parsedEncoding,
            ["columns"] = parsedColumns.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ice"] = parsedIce.ToString(),
            ["bytes"] = parsedBytes is null ? "" : Convert.ToBase64String(parsedBytes)
        });
        result.DraftFactory = document => new(2, document, null, null, sourceText, "ansi", new()
        {
            ["encoding"] = EncodingName,
            ["columns"] = ((int)columns.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ice"] = ice.IsOn.ToString(),
            ["bytes"] = sourceBytes is null ? "" : Convert.ToBase64String(sourceBytes)
        });
        input.TextChanged += (_, _) =>
        {
            var normalized = input.Text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (applying || normalized == sourceText.Replace("\r\n", "\n").Replace('\r', '\n')) return;
            sourceText = normalized; sourceBytes = null; metadata.Text = "";
            result.InputChanged();
            pending?.Cancel(); version++; progress.IsActive = false; info.Text = "原文已修改，点击“查看”更新结果。";
        };
        encoding.SelectionChanged += async (_, _) => { if (!applying) { result.InputChanged(); if (sourceBytes is not null) await App.Window.Guard(Parse); } };
        columns.ValueChanged += (_, _) => { if (!applying) result.InputChanged(); };
        ice.Toggled += (_, _) => { if (!applying) result.InputChanged(); };
        Unloaded += (_, _) => { pending?.Cancel(); version++; progress.IsActive = false; };
        Children.Add(Ui.Page(Ui.Heading("ANSI 查看器", ""), Ui.Workspace(Ui.Card(panel), result)));
        AllowDrop = true;
        DragOver += (_, args) => { if (args.DataView.Contains(StandardDataFormats.StorageItems)) args.AcceptedOperation = DataPackageOperation.Copy; };
        Drop += async (_, args) => await App.Window.Guard(async () =>
        {
            if (args.DataView.Contains(StandardDataFormats.StorageItems) && (await args.DataView.GetStorageItemsAsync()).FirstOrDefault() is StorageFile file) await Open(file.Path);
        });
    }
    private string EncodingName => encoding.SelectedIndex switch { 1 => "UTF-8", 2 => "CP437", _ => "Auto" };
    private void SetText(string text)
    {
        var previous = applying; applying = true; try { sourceText = text; input.Text = text; } finally { applying = previous; }
    }
    private async Task Pick()
    {
        var picker = new FileOpenPicker(); foreach (var extension in new[] { ".ans", ".ansi", ".ice", ".nfo", ".asc", ".diz", ".lit", ".drk", ".txt" }) picker.FileTypeFilter.Add(extension);
        App.Window.InitializePicker(picker); var file = await picker.PickSingleFileAsync(); if (file is not null) await Open(file.Path);
    }
    public async Task Open(string path)
    {
        pending?.Cancel(); var current = ++version;
        var selected = EncodingName; byte[] bytes;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true))
        {
            if (stream.Length > AnsiArt.InputLimit) throw new ArgumentException("ANSI 文件超过 4 MB 限制。");
            bytes = new byte[(int)stream.Length]; await stream.ReadExactlyAsync(bytes);
        }
        var source = await Task.Run(() => AnsiArt.Decode(bytes, selected));
        if (current != version) return;
        sourceBytes = bytes; title = Path.GetFileNameWithoutExtension(path); SetText(source.Text);
        applying = true;
        try { if (source.Metadata?.Width is >= 20 and <= 300) columns.Value = source.Metadata.Width; ice.IsOn = source.Metadata?.IceColors ?? path.EndsWith(".ice", StringComparison.OrdinalIgnoreCase); }
        finally { applying = false; }
        await Parse();
    }
    private async Task Paste()
    {
        pending?.Cancel(); var current = ++version;
        var data = Clipboard.GetContent(); if (!data.Contains(StandardDataFormats.Text)) throw new ArgumentException("剪贴板中没有文本。");
        var text = await data.GetTextAsync(); if (Encoding.UTF8.GetByteCount(text) > AnsiArt.InputLimit) throw new ArgumentException("粘贴文本超过 4 MB 限制。");
        if (current != version) return;
        sourceBytes = null; title = "Pasted ANSI"; SetText(text); await Parse();
    }
    private async Task Sample()
    {
        sourceBytes = null; title = "ANSI color sample";
        SetText("\x1b[1;36mASCII STUDIO\x1b[0m\r\n\x1b[37;44m  ANSI color preview  \x1b[0m\r\n\x1b[38;5;214m16 / 256 / TrueColor\x1b[0m\r\n\x1b[38;2;120;220;180m╔═══════════════════╗\r\n║   COLOR + TEXT    ║\r\n╚═══════════════════╝\x1b[0m");
        await Parse();
    }
    private async Task Parse()
    {
        if (!double.IsFinite(columns.Value) || columns.Value is < 20 or > 300 || columns.Value != Math.Truncate(columns.Value)) throw new ArgumentException("请输入 20–300 的整数列数。");
        if (sourceBytes is null && Encoding.UTF8.GetByteCount(sourceText) > AnsiArt.InputLimit) throw new ArgumentException("ANSI 原文超过 4 MB 限制。");
        pending?.Cancel(); var cancellation = new CancellationTokenSource(); pending = cancellation; var current = ++version;
        var bytes = sourceBytes; var raw = sourceText; var selected = EncodingName; var width = (int)columns.Value; var useIce = ice.IsOn; var name = title; progress.IsActive = true;
        try
        {
            var source = bytes is null ? new AnsiSource(raw, "Unicode 粘贴", null, 0) : await Task.Run(() => AnsiArt.Decode(bytes, selected));
            var parsed = await Task.Run(() => AnsiArt.Parse(source.Text, width, useIce, source.Metadata?.Title is { Length: > 0 } t ? t : name, cancellation.Token));
            if (current != version) return;
            var metrics = FontCatalog.Measure(result.CharacterFontFamily);
            var document = parsed.Document with { FontFamily = result.CharacterFontFamily, CellWidth = metrics.Width, CellHeight = metrics.Height };
            parsedBytes = bytes; parsedText = source.Text; parsedEncoding = selected; parsedColumns = width; parsedIce = useIce; SetText(source.Text);
            result.ShowColorPreview(true); await result.SetDocument(document);
            if (current != version) return;
            info.Text = $"{source.Encoding} · {document.Width} × {document.Height} · 忽略 {parsed.IgnoredSequences} · 未完成 {parsed.IncompleteSequences}" + (source.MetadataWarnings > 0 ? " · 元数据异常" : "") + (parsed.HasWideCharacters ? " · 双列 Unicode 网格" : "");
            metadata.Text = source.Metadata is { } m ? $"SAUCE · {m.Title} · {m.Author} · {m.Group} · {m.Date}" + (m.Comments.Length > 0 ? "\n" + string.Join('\n', m.Comments) : "") : "";
        }
        finally { if (current == version) progress.IsActive = false; if (ReferenceEquals(pending, cancellation)) pending = null; cancellation.Dispose(); }
    }
    public async Task LoadProject(StudioProject project)
    {
        var parameters = project.Parameters; applying = true;
        try
        {
            encoding.SelectedIndex = parameters?.GetValueOrDefault("encoding") switch { "UTF-8" => 1, "CP437" => 2, _ => 0 };
            columns.Value = int.TryParse(parameters?.GetValueOrDefault("columns"), out var width) && width is >= 20 and <= 300 ? width : 80;
            ice.IsOn = bool.TryParse(parameters?.GetValueOrDefault("ice"), out var useIce) && useIce;
            var encoded = parameters?.GetValueOrDefault("bytes");
            if (encoded?.Length > (AnsiArt.InputLimit + 2L) / 3 * 4) throw new ArgumentException("项目的 ANSI 来源超过 4 MB 限制。");
            sourceBytes = string.IsNullOrEmpty(encoded) ? null : Convert.FromBase64String(encoded);
            if (sourceBytes?.Length > AnsiArt.InputLimit) throw new ArgumentException("项目的 ANSI 来源超过 4 MB 限制。");
            SetText(project.SourceText ?? ""); title = project.Document.Title;
        }
        finally { applying = false; }
        parsedText = sourceText; parsedBytes = sourceBytes; parsedEncoding = EncodingName; parsedColumns = (int)columns.Value; parsedIce = ice.IsOn;
        result.ShowColorPreview(true); await result.LoadDocument(project); info.Text = "项目已恢复，点击“查看”可用原文重新解析。";
    }
}
