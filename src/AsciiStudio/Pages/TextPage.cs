using System.Reflection;
using Figgle;
using Figgle.Fonts;
using AsciiStudio.Controls;
using AsciiStudio.Core;
using AsciiStudio.Services;
using Microsoft.UI.Xaml.Controls;

namespace AsciiStudio.Pages;

public sealed class TextPage : Grid
{
    private readonly ResultPane result = new();
    private readonly TextBox input = new() { Text = "ASCII STUDIO", AcceptsReturn = true, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, MinHeight = 130, MaxLength = 2000 };
    private readonly ComboBox mode = Ui.Choice(["FIGlet 艺术字", "系统字体 → 字符画（支持中文）"]);
    private readonly ComboBox figFont;
    private readonly ComboBox systemFont = Ui.Choice(["Microsoft YaHei UI", "Segoe UI", "Arial", "Consolas", "SimSun"]);
    private readonly NumberBox columns = new NumberBox() { Value = 120, Minimum = 16, Maximum = 600 };
    private readonly ComboBox border = Ui.Choice(["无边框", "ASCII 线框", "双线框", "星号边框"]);
    private readonly CheckBox trim = new() { Content = "裁掉外围空白", IsChecked = true };
    private readonly TextBox replacement = new() { PlaceholderText = "留空则保留空格", MaxLength = 1 };
    private string? generatedSource;
    private Dictionary<string, string>? generatedParameters;
    private int generatedColumns = 120;
    private readonly PropertyInfo[] fonts = typeof(FiggleFonts).GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(FiggleFont)).OrderBy(p => p.Name).ToArray();
    public TextPage()
    {
        figFont = Ui.Choice(fonts.Select(f => f.Name), Math.Max(0, Array.FindIndex(fonts, p => p.Name == "Standard")));
        var generate = Ui.AsyncButton("生成字符画", Generate, true); Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(generate, "TextGenerate");
        var p = Ui.Stack(); p.Children.Add(Ui.Field("文字内容", input)); p.Children.Add(Ui.Field("转换方式", mode));
        var figFontField = Ui.Field($"FIGlet 字体 · {fonts.Length} 款", figFont);
        var systemFontField = Ui.Field("系统字体", systemFont);
        p.Children.Add(figFontField); p.Children.Add(systemFontField); p.Children.Add(generate);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(figFont, "TextFigletFont");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(systemFont, "TextSystemFont");
        var fontSettings = Ui.Stack(); fontSettings.Width = 340;
        fontSettings.Children.Add(Ui.Field("字符画列数（系统字体模式）", columns));
        result.AddSettings("尺寸", fontSettings, "TextFontSettings");
        var layoutSettings = Ui.Stack(); layoutSettings.Width = 260;
        layoutSettings.Children.Add(Ui.Field("边框", border)); layoutSettings.Children.Add(trim); layoutSettings.Children.Add(Ui.Field("替换空格", replacement));
        result.AddSettings("排版", layoutSettings, "TextLayoutSettings");
        p.Children.Add(Ui.Text("中文请选择系统字体模式。FIGlet 字体通常以拉丁字符为主。修改结果时，复制和导出使用修改后的内容。", 12, true));
        Children.Add(Ui.Page(Ui.Heading("文字转换", "把一句话变成标题、签名，或一幅文字作品。"), Ui.Workspace(Ui.Card(p), result)));
        void UpdateFontMode()
        {
            figFontField.Visibility = mode.SelectedIndex == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
            systemFontField.Visibility = mode.SelectedIndex == 1 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
            columns.IsEnabled = mode.SelectedIndex == 1;
        }
        mode.SelectionChanged += (_, _) => UpdateFontMode(); UpdateFontMode();
        result.ProjectFactory = doc => new(1, doc, new ConversionOptions { Columns = generatedColumns }, null, generatedSource, "text", generatedParameters);
    }
    private async Task Generate()
    {
        var text = input.Text; if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("先输入一些文字。");
        var selectedMode = mode.SelectedIndex; var selectedFont = figFont.SelectedIndex; var family = systemFont.SelectedItem?.ToString() ?? "Microsoft YaHei UI";
        var selectedBorder = border.SelectedIndex; var crop = trim.IsChecked == true; var replace = replacement.Text;
        if (!double.IsFinite(columns.Value)) throw new ArgumentException("请输入有效列数。"); var col = (int)columns.Value;
        string output;
        if (selectedMode == 0)
        {
            if (text.Any(c => c > 127)) throw new ArgumentException("当前 FIGlet 模式只支持 ASCII 输入；中文或 emoji 请切换系统字体模式。");
            var font = (FiggleFont)fonts[selectedFont].GetValue(null)!;
            output = await Task.Run(() => font.Render(text).TrimEnd('\r', '\n'));
        }
        else
        {
            output = await Task.Run(() => { var raster = ImagingService.RasterizeText(text, family, 96, true); return ImageConverter.Convert(raster.pixels, raster.width, raster.height, new ConversionOptions { Columns = col }).Text; });
        }
        if (crop) output = TextUtilities.TrimCanvas(output);
        if (replace.Length == 1) output = output.Replace(' ', replace[0]);
        if (selectedBorder > 0) output = Generators.Border(output, selectedBorder);
        generatedSource = text; generatedColumns = col; generatedParameters = new() { ["mode"] = selectedMode.ToString(), ["font"] = fonts[selectedFont].Name, ["systemFont"] = family, ["border"] = selectedBorder.ToString(), ["trim"] = crop.ToString(), ["replacement"] = replace };
        await result.SetDocument(AsciiDocument.FromText(output, text.Split('\n')[0].Length > 32 ? text[..32] : text.Split('\n')[0]));
    }
    public async Task LoadProject(StudioProject project)
    {
        input.Text = project.SourceText ?? ""; if (project.Options is not null) columns.Value = project.Options.Columns;
        generatedSource = project.SourceText; generatedParameters = project.Parameters; generatedColumns = project.Options?.Columns ?? 120;
        if (project.Parameters is { } p)
        {
            if (p.TryGetValue("mode", out var m) && int.TryParse(m, out var mi) && mi is >= 0 and <= 1) mode.SelectedIndex = mi;
            if (p.TryGetValue("font", out var f) && figFont.Items.Contains(f)) figFont.SelectedItem = f;
            if (p.TryGetValue("systemFont", out var sf) && systemFont.Items.Contains(sf)) systemFont.SelectedItem = sf;
            if (p.TryGetValue("border", out var b) && int.TryParse(b, out var bi) && bi is >= 0 and <= 3) border.SelectedIndex = bi;
            if (p.TryGetValue("trim", out var t) && bool.TryParse(t, out var tr)) trim.IsChecked = tr;
            if (p.TryGetValue("replacement", out var r)) replacement.Text = r.Length <= 1 ? r : "";
        }
        await result.SetDocument(project.Document);
    }
}
