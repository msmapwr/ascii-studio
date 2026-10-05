using System.Text.Json;
using Charloom.Controls;
using Charloom.Core;
using Charloom.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace Charloom.Pages;

public sealed class TextPage : Grid, IProjectSessionPage
{
    private readonly ResultPane result = new();
    public ResultPane ResultPane => result;
    public string SessionMode => "text";
    public event Action<bool>? DirtyChanged { add => result.DirtyChanged += value; remove => result.DirtyChanged -= value; }
    public event Action<AsciiDocument>? DocumentChanged { add => result.DocumentChanged += value; remove => result.DocumentChanged -= value; }
    public void SetSession(string id, string? path) => result.SetSession(id, path);
    public Task<bool> SaveProjectAsync() => result.SaveProjectAsync();
    public Task SaveRecoveryAsync() => result.SaveRecoveryAsync();
    private readonly TextBox input = new() { Text = "ASCII STUDIO", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80, MaxLength = 2000 };
    private readonly ComboBox mode = Ui.Choice(["FIGlet 艺术字", "系统字体 → 字符画（支持中文）"]);
    private readonly AutoSuggestBox figFont = new() { Text = "standard", PlaceholderText = "搜索字体", QueryIcon = new SymbolIcon(Symbol.Find) };
    private TextFontEntry[] fonts = TextFontLibrary.Entries();
    private string selectedFont = "builtin:standard";
    private bool loading;
    private int page;
    private readonly TextCreationController controller = new();
    private readonly TextBlock previewSample = Ui.Text("abc", 18), fontPreview = new() { FontFamily = new FontFamily("Consolas"), FontSize = 8 };
    private readonly ScrollViewer fontPreviewScroll;
    private readonly FontPicker systemFont = new("TextSystemFont");
    private readonly ComboBox systemStyle = Ui.Choice(["标准", "细线", "点阵", "方块", "高密度"]);
    private readonly NumberBox columns = Number(120, 16, 600), letterSpacing = Number(0, 0, 20), lineSpacing = Number(0, 0, 20), maximumWidth = Number(0, 0, 2000), paddingX = Number(1, 0, 30), paddingY = Number(0, 0, 30), stroke = Number(0, 0, 12);
    private readonly ComboBox alignment = Ui.Choice(["左对齐", "居中", "右对齐"]), horizontal = Ui.Choice(["字体默认", "完整宽度", "挤紧", "规则重叠"]), vertical = Ui.Choice(["字体默认", "完整高度", "挤紧", "规则重叠"]), border = Ui.Choice(["无边框", "ASCII 线框", "双线框", "星号边框"]), weight = Ui.Choice(["常规", "粗体"], 1), fill = Ui.Choice(["实心", "空心"]), preset = Ui.Choice(["自定义", "中文清晰", "混排均衡", "紧凑"]);
    private readonly CheckBox wrap = new() { Content = "自动换行" }, trim = new() { Content = "裁掉外围空白", IsChecked = true }, allowMissing = new() { Content = "仍然生成（允许系统回退或缺字）" }, favoritesOnly = new() { Content = "仅收藏字体" }, currentSample = new() { Content = "使用当前输入前24字" };
    private readonly TextBox borderTitle = new() { MaxLength = 100 }, replacement = new() { MaxLength = 1, PlaceholderText = "留空则保留空格" };
    private readonly InfoBar warning = new() { IsClosable = true, Severity = InfoBarSeverity.Warning };
    private readonly TextBlock status = Ui.Text("", 12), details = Ui.Text("", 12), gridPage = Ui.Text("", 12);
    private readonly GridView fontGrid = new() { Height = 320, SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    private CancellationTokenSource? gridPending;
    private static NumberBox Number(double value, int min, int max) => new() { Value = value, Minimum = min, Maximum = max, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private static int Integer(NumberBox n) => double.IsFinite(n.Value) && n.Value == Math.Truncate(n.Value) ? checked((int)n.Value) : throw new ArgumentException("请输入整数参数。");
    private static void Id(DependencyObject e, string id) => Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(e, id);
    private static FrameworkElement Field(string label, UIElement element, string id) { var field = Ui.Field(label, element); Id(element, id); return field; }
    public TextPage()
    {
        result.RestoreProject = LoadProject;
        figFont.TextChanged += (_, _) => { figFont.ItemsSource = fonts.Where(f => f.Name.Contains(figFont.Text, StringComparison.OrdinalIgnoreCase)).Select(f => f.Name).ToArray(); if (fonts.FirstOrDefault(f => f.Name.Equals(figFont.Text, StringComparison.OrdinalIgnoreCase)) is { } f && f.Id != selectedFont) SelectFont(f.Id); };
        figFont.GotFocus += (_, _) => figFont.ItemsSource = fonts.Select(f => f.Name).ToArray();
        figFont.SuggestionChosen += (_, e) => SelectFont(e.SelectedItem?.ToString());
        figFont.QuerySubmitted += (_, e) => SelectFont(e.ChosenSuggestion?.ToString() ?? fonts.FirstOrDefault(f => f.Name.Contains(e.QueryText, StringComparison.OrdinalIgnoreCase))?.Id);
        figFont.LostFocus += (_, _) => figFont.Text = fonts.FirstOrDefault(f => f.Id == selectedFont)?.Name ?? "缺失的字体";
        figFont.Loaded += (_, _) => { if (Ui.FindDescendant<TextBox>(figFont) is { } search) Id(search, "TextFontSearchInput"); };
        var p = Ui.Stack(); p.Children.Add(Field("文字内容", input, "Field_文字内容")); p.Children.Add(Field("转换方式", mode, "TextMode"));
        var figField = Field("FIGlet 字体", figFont, "TextFigletFont"); var sysField = Ui.Field("系统字体", systemFont); var styleField = Field("中文字画风格", systemStyle, "TextSystemStyle"); p.Children.Add(figField); p.Children.Add(sysField); p.Children.Add(styleField); systemFont.Select("Microsoft YaHei UI");
        fontPreviewScroll = new() { Content = fontPreview, Height = 100, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var sample = Ui.Stack(4); sample.Children.Add(previewSample); sample.Children.Add(fontPreviewScroll); p.Children.Add(sample); Id(previewSample, "FontPreviewSample"); Id(fontPreview, "FontPreviewArt");
        var generate = Ui.AsyncButton("生成字符画", Generate, true); Id(generate, "TextGenerate"); p.Children.Add(generate);
        AddSettings("尺寸", "TextFontSettings", Field("系统字体列数", columns, "TextColumns"));
        AddSettings("排版", "TextLayoutSettings", Field("字距", letterSpacing, "TextLetterSpacing"), Field("行距", lineSpacing, "TextLineSpacing"), Field("对齐", alignment, "TextAlignment"), Field("最大宽度 · 0为不限", maximumWidth, "TextMaximumWidth"), Field("横向排版", horizontal, "TextHorizontal"), Field("纵向排版", vertical, "TextVertical"), wrap, trim, Field("替换空格", replacement, "TextReplacement"), Ui.Text("字距大于0时使用完整宽度；行距大于0时不进行纵向重叠。", 12, true)); Id(wrap, "TextWrap");
        AddSettings("边框", "TextBorderSettings", Field("边框", border, "TextBorder"), Field("横向内边距", paddingX, "TextPaddingX"), Field("纵向内边距", paddingY, "TextPaddingY"), Field("标题", borderTitle, "TextBorderTitle"));
        AddSettings("中文字形", "TextRasterSettings", Field("可读性预设", preset, "TextReadability"), Field("字重", weight, "TextWeight"), Field("描边宽度", stroke, "TextStroke"), Field("填充", fill, "TextFill"), allowMissing); Id(allowMissing, "TextAllowMissing");
        var library = Ui.Stack(8); library.Width = 640;
        var fontActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        fontActions.Children.Add(Ui.AsyncButton("收藏／取消收藏", async () => { await TextFontLibrary.ToggleFavorite(selectedFont); UpdateDetails(); await LoadGrid(); }));
        fontActions.Children.Add(Ui.AsyncButton("导入 .flf", ImportFont)); library.Children.Add(fontActions);
        currentSample.Content = "当前输入前24字";
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; filters.Children.Add(favoritesOnly); filters.Children.Add(currentSample); library.Children.Add(filters);
        library.Children.Add(fontGrid); library.Children.Add(gridPage);
        var paging = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        paging.Children.Add(Ui.AsyncButton("上一页字体", async () => { page = Math.Max(0, page - 1); await LoadGrid(); })); paging.Children.Add(Ui.AsyncButton("下一页字体", async () => { page++; await LoadGrid(); })); library.Children.Add(paging);
        library.Children.Add(new Expander { Header = "字体详情", Content = new ScrollViewer { Content = details, MaxHeight = 180 }, HorizontalAlignment = HorizontalAlignment.Stretch }); Id(details, "TextFontDetails");
        Id(fontGrid, "TextFontGrid"); Id(gridPage, "TextGridPage"); Id(favoritesOnly, "TextFavoritesOnly"); Id(currentSample, "TextGridCurrentSample");
        SizeChanged += (_, _) => { if (XamlRoot is not null) fontGrid.Height = Math.Clamp(XamlRoot.Size.Height - 430, 80, 320); };
        var flyout = result.AddSettings("字体库", library, "TextLibrarySettings", useAvailableHeight: true); flyout.Opened += async (_, _) => await App.Window.Guard(LoadGrid); flyout.Closed += (_, _) => gridPending?.Cancel();
        foreach (var check in new[] { favoritesOnly, currentSample }) { check.Checked += async (_, _) => { page = 0; await App.Window.Guard(LoadGrid); }; check.Unchecked += async (_, _) => { page = 0; await App.Window.Guard(LoadGrid); }; }
        fontGrid.ItemClick += (_, e) => { if (e.ClickedItem is GridViewItem { Tag: string id }) SelectFont(id); };
        fontGrid.SelectionChanged += (_, e) => { if (e.AddedItems.FirstOrDefault() is GridViewItem { Tag: string id }) SelectFont(id); };
        var messages = Ui.Stack(4); messages.Children.Add(warning); messages.Children.Add(status); result.AddStatus(messages); Id(warning, "TextWarning"); Id(status, "TextStatus");
        Children.Add(Ui.Page(Ui.Heading("文字转换", ""), Ui.Workspace(Ui.Card(p), result)));
        void UpdateMode() { figField.Visibility = mode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; sysField.Visibility = styleField.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; columns.IsEnabled = weight.IsEnabled = stroke.IsEnabled = fill.IsEnabled = preset.IsEnabled = allowMissing.IsEnabled = mode.SelectedIndex == 1; horizontal.IsEnabled = vertical.IsEnabled = mode.SelectedIndex == 0; QueuePreview(true); }
        mode.SelectionChanged += (_, _) => UpdateMode(); UpdateMode(); systemFont.Changed += _ => QueuePreview(true); systemStyle.SelectionChanged += (_, _) => QueuePreview(true); input.TextChanged += (_, _) => DraftChanged();
        foreach (var number in new[] { columns, letterSpacing, lineSpacing, maximumWidth, paddingX, paddingY, stroke }) number.ValueChanged += (_, _) => DraftChanged();
        foreach (var combo in new[] { alignment, horizontal, vertical, border, weight, fill }) combo.SelectionChanged += (_, _) => DraftChanged();
        foreach (var check in new[] { trim, wrap, allowMissing }) { check.Checked += (_, _) => DraftChanged(); check.Unchecked += (_, _) => DraftChanged(); }
        replacement.TextChanged += (_, _) => DraftChanged(); borderTitle.TextChanged += (_, _) => DraftChanged();
        preset.SelectionChanged += (_, _) => { if (loading || preset.SelectedIndex == 0) return; columns.Value = preset.SelectedIndex == 1 ? 200 : preset.SelectedIndex == 2 ? 160 : 96; weight.SelectedIndex = preset.SelectedIndex == 3 ? 0 : 1; stroke.Value = preset.SelectedIndex == 1 ? 1 : 0; fill.SelectedIndex = 0; letterSpacing.Value = preset.SelectedIndex == 1 ? 2 : preset.SelectedIndex == 2 ? 1 : 0; lineSpacing.Value = preset.SelectedIndex == 3 ? 0 : 1; QueuePreview(true); };
        Loaded += (_, _) => QueuePreview(); Unloaded += (_, _) => { controller.Previews.Cancel(); gridPending?.Cancel(); controller.Operations.Cancel(); };
        result.DraftFactory = doc => TextProjectMapper.Project(doc, Integer(columns), input.Text, Parameters()); result.ProjectFactory = controller.Project; UpdateDetails();
    }
    private void AddSettings(string label, string id, params UIElement[] content) { var panel = Ui.Stack(); panel.Width = 340; foreach (var e in content) panel.Children.Add(e); result.AddSettings(label, panel, id); }
    private TextArtOptions Layout() => new() { LetterSpacing = Integer(letterSpacing), LineSpacing = Integer(lineSpacing), MaximumWidth = Integer(maximumWidth), Wrap = wrap.IsChecked == true, Alignment = (ArtAlignment)alignment.SelectedIndex, Horizontal = (ArtPacking)horizontal.SelectedIndex, Vertical = (ArtPacking)vertical.SelectedIndex, Border = border.SelectedIndex, PaddingX = Integer(paddingX), PaddingY = Integer(paddingY), Title = borderTitle.Text, Trim = trim.IsChecked == true, Replacement = replacement.Text };
    private TextRasterOptions Raster() => new(systemFont.SelectedFont, Integer(columns), systemStyle.SelectedIndex, weight.SelectedIndex == 1, stroke.Value, fill.SelectedIndex == 0);
    private Dictionary<string, string> Parameters() => new() { ["mode"] = mode.SelectedIndex.ToString(), ["font"] = selectedFont, ["fontDigest"] = fonts.FirstOrDefault(f => f.Id == selectedFont)?.Digest ?? "", ["systemFont"] = systemFont.SelectedFont, ["systemStyle"] = systemStyle.SelectedIndex.ToString(), ["border"] = border.SelectedIndex.ToString(), ["trim"] = (trim.IsChecked == true).ToString(), ["replacement"] = replacement.Text, ["layout"] = JsonSerializer.Serialize(Layout()), ["raster"] = JsonSerializer.Serialize(Raster()), ["readabilityPreset"] = preset.SelectedIndex.ToString(), ["allowMissing"] = (allowMissing.IsChecked == true).ToString() };
    private void DraftChanged() { if (IsLoaded && !loading) result.InputChanged(); }
    private void SelectFont(string? value) { if (value is null) return; var found = fonts.FirstOrDefault(f => f.Id == value || f.Name.Equals(value, StringComparison.OrdinalIgnoreCase)); if (found is null) return; selectedFont = found.Id; if (mode.SelectedIndex == 0) warning.IsOpen = false; if (figFont.Text != found.Name) figFont.Text = found.Name; UpdateDetails(); QueuePreview(true); }
    private void UpdateDetails() { var e = fonts.FirstOrDefault(f => f.Id == selectedFont); details.Text = e is null ? "项目字体缺失，请导入或选择字体。" : $"{e.Name} · {e.Source} · 高度{e.Height} · 基线{e.Baseline}\n布局标记{e.Layout} · {(TextFontLibrary.IsFavorite(selectedFont) ? "已收藏" : "未收藏")}\nSHA256 {e.Digest}\n{DocumentViewIndex.Prefix(e.Comments, 2000)}"; }
    private async Task ImportFont() { var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".flf"); App.Window.InitializePicker(picker); var file = await picker.PickSingleFileAsync(); if (file is null) return; var e = await TextFontLibrary.Import(file.Path); fonts = TextFontLibrary.Entries(); SelectFont(e.Id); page = 0; await LoadGrid(); status.Text = "字体已导入本地库。"; }
    private async Task LoadGrid()
    {
        gridPending?.Cancel(); var cts = new CancellationTokenSource(); gridPending = cts; var token = cts.Token;
        try
        {
            var candidates = fonts.Where(f => favoritesOnly.IsChecked != true || TextFontLibrary.IsFavorite(f.Id)).ToArray(); var total = Math.Max(1, (candidates.Length + 11) / 12); page = Math.Clamp(page, 0, total - 1); gridPage.Text = $"第{page + 1}/{total}页 · {candidates.Length}款"; fontGrid.Items.Clear(); var sample = currentSample.IsChecked == true ? string.Concat(TextArtLayout.Elements(input.Text).Take(24)).Replace('\n', ' ') : "abc";
            foreach (var e in candidates.Skip(page * 12).Take(12))
            {
                var art = sample.Any(c => c > 127) ? "当前输入含非 ASCII 字符\n请使用 abc 样本" : await Task.Run(() => TextFontLibrary.Render(e.Id, sample, new() { Trim = true }, token), token);
                token.ThrowIfCancellationRequested();
                var panel = Ui.Stack(4); panel.Width = 220;
                panel.Children.Add(Ui.Text((TextFontLibrary.IsFavorite(e.Id) ? "★ " : "") + e.Name, 14));
                panel.Children.Add(new TextBlock { Text = DocumentViewIndex.Prefix(art, 2000), FontFamily = new FontFamily("Consolas"), FontSize = 8, MaxHeight = 110, TextWrapping = TextWrapping.NoWrap });
                var item = new GridViewItem { Content = panel, Tag = e.Id }; Id(item, "FontTile_" + e.Name); fontGrid.Items.Add(item);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ArgumentException e) { status.Text = e.Message; }
        finally { if (ReferenceEquals(gridPending, cts)) gridPending = null; cts.Dispose(); }
    }
    private void QueuePreview(bool changed = false) { if (changed) DraftChanged(); _ = App.Window.Guard(UpdatePreview); }
    private async Task UpdatePreview()
    {
        using var operation = controller.Previews.Begin();
        try
        {
            var chinese = mode.SelectedIndex == 1; var raster = Raster();
            var rendered = await controller.Preview(chinese, selectedFont, raster, operation.Token);
            if (!operation.IsCurrent || rendered is null) return;
            previewSample.Text = chinese ? "测试" : "abc";
            previewSample.FontFamily = new FontFamily(chinese ? raster.Family : "Segoe UI");
            fontPreview.Text = rendered; fontPreview.FontSize = chinese ? 12 : 8;
            fontPreviewScroll.Height = chinese ? 180 : 100; await MotionService.Fade(fontPreview, 0, 1, 140);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { }
    }
    private async Task Generate()
    {
        using var operation = controller.Operations.Begin(); var token = operation.Token;
        try
        {
            var family = result.CharacterFontFamily; var metrics = FontCatalog.Measure(family);
            var request = new TextCreationRequest(input.Text, mode.SelectedIndex, selectedFont, Layout(), Raster(),
                family, metrics.Width, metrics.Height, Parameters());
            var allow = allowMissing.IsChecked == true; status.Text = "正在生成…";
            if (request.Mode == 1)
            {
                var missing = await controller.Missing(request, token); if (!operation.IsCurrent) return;
                warning.IsOpen = missing.Length > 0; warning.Title = "所选字体可能缺少字形";
                warning.Message = "缺字或需回退：" + string.Join(" ", missing);
                if (missing.Length > 0 && !allow) { status.Text = "请更换字体，或在中文字形中选择仍然生成。"; return; }
            }
            var document = await controller.Generate(request, token); if (!operation.IsCurrent) return;
            controller.Accept(request); result.SetReadablePreview(request.Mode == 1);
            await result.SetDocument(document, cancellationToken: token);
            if (operation.IsCurrent) status.Text = "生成完成";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) when (e is ArgumentException or Figgle.FiggleException)
        { if (operation.IsCurrent) { warning.Title = "无法生成"; warning.Message = e.Message; warning.IsOpen = true; status.Text = "请调整设置后重新生成。"; } }
    }
    public async Task LoadProject(StudioProject project)
    {
        var restored = TextProjectMapper.Restore(project);
        controller.Restore(project); loading = true;
        try
        {
            input.Text = project.SourceText ?? ""; columns.Value = restored.Raster.Columns; mode.SelectedIndex = restored.Mode;
            selectedFont = restored.Font.StartsWith("user:", StringComparison.Ordinal) ? restored.Font : TextFontLibrary.ResolveId(restored.Font, fonts);
            figFont.Text = fonts.FirstOrDefault(f => f.Id == selectedFont)?.Name ?? "缺失的字体"; systemStyle.SelectedIndex = restored.Style;
            var o = restored.Layout; letterSpacing.Value = o.LetterSpacing; lineSpacing.Value = o.LineSpacing; maximumWidth.Value = o.MaximumWidth; alignment.SelectedIndex = (int)o.Alignment; horizontal.SelectedIndex = (int)o.Horizontal; vertical.SelectedIndex = (int)o.Vertical; wrap.IsChecked = o.Wrap; border.SelectedIndex = o.Border; paddingX.Value = o.PaddingX; paddingY.Value = o.PaddingY; borderTitle.Text = o.Title; trim.IsChecked = o.Trim; replacement.Text = o.Replacement;
            var ro = restored.Raster; var missingSystemFont = !FontCatalog.Names.Contains(ro.Family);
            systemFont.Select(missingSystemFont ? FontCatalog.Names.FirstOrDefault(f => f == "Microsoft YaHei UI") ?? FontCatalog.Names.First() : ro.Family);
            weight.SelectedIndex = ro.Bold ? 1 : 0; stroke.Value = ro.Stroke; fill.SelectedIndex = ro.Filled ? 0 : 1; preset.SelectedIndex = restored.Preset;
            allowMissing.IsChecked = restored.AllowMissing; warning.IsOpen = mode.SelectedIndex == 0 ? !TextFontLibrary.Available(selectedFont) : missingSystemFont;
            warning.Title = "项目字体缺失"; warning.Message = "已保留原结果；导入对应字体或选择其他字体后可生成。"; UpdateDetails(); result.SetReadablePreview(mode.SelectedIndex == 1); await result.LoadDocument(project);
        }
        finally { loading = false; QueuePreview(); }
    }
}
