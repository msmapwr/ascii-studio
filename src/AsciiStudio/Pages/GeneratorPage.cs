using AsciiStudio.Controls;
using AsciiStudio.Core;
using Microsoft.UI.Xaml.Controls;

namespace AsciiStudio.Pages;

public sealed class GeneratorPage : Grid
{
    private readonly ResultPane result = new();
    private readonly ComboBox kind = Ui.Choice(["文字边框", "分隔线", "迷宫", "星空", "棋盘", "斜纹", "密度渐变"]);
    private readonly ComboBox style = Ui.Choice(["ASCII / 虚线", "双线 / 点线", "星号 / 等号", "波浪"]);
    private readonly TextBox text = new() { Text = "Hello, ASCII!", AcceptsReturn = true, MinHeight = 100 };
    private readonly NumberBox width = new() { Value = 60, Minimum = 8, Maximum = 300 };
    private readonly NumberBox height = new() { Value = 20, Minimum = 3, Maximum = 100 };
    private readonly NumberBox seed = new() { Value = 42, Minimum = 0, Maximum = int.MaxValue };
    public GeneratorPage()
    {
        var p = Ui.Stack(); p.Children.Add(Ui.Field("生成器", kind));
        p.Children.Add(Ui.Field("边框内容", text));
        p.Children.Add(Ui.AsyncButton("生成", Generate, true));
        var parameters = Ui.Stack();
        parameters.Children.Add(Ui.SettingsGrid(Ui.Field("宽度", width), Ui.Field("高度", height), Ui.Field("边框 / 分隔线样式", style), Ui.Field("随机种子", seed)));
        parameters.Children.Add(Ui.Button("换一个随机种子", () => seed.Value = Random.Shared.Next(int.MaxValue)));
        result.AddSettings("生成参数", parameters, "GeneratorSettings");
        p.Children.Add(Ui.Text("相同种子产生相同结果。迷宫尺寸最多 100 × 80 个单元，入口在左上，出口在右下。", 12, true));
        Children.Add(Ui.Page(Ui.Heading("生成器", "用几项参数，生成可编辑的字符作品。"), Ui.Workspace(Ui.Card(p), result)));
        kind.SelectionChanged += (_, _) => UpdateControls(); UpdateControls();
    }
    private void UpdateControls()
    { text.IsEnabled = kind.SelectedIndex == 0; style.IsEnabled = kind.SelectedIndex < 2; width.IsEnabled = kind.SelectedIndex != 0; height.IsEnabled = kind.SelectedIndex > 1; seed.IsEnabled = kind.SelectedIndex is 2 or 3; }
    private async Task Generate()
    {
        if (!double.IsFinite(width.Value) || !double.IsFinite(height.Value) || !double.IsFinite(seed.Value)) throw new ArgumentException("请输入有效的尺寸和随机种子。");
        var w = (int)width.Value; var h = (int)height.Value; var s = (int)seed.Value;
        var output = kind.SelectedIndex switch
        {
            0 => Generators.Border(text.Text, Math.Min(style.SelectedIndex + 1, 3)),
            1 => Generators.Divider(w, style.SelectedIndex),
            2 => Generators.Maze(w, h, s),
            _ => Generators.Pattern(w, h, kind.SelectedIndex - 3, s)
        };
        await result.SetDocument(AsciiDocument.FromText(output, kind.SelectedItem?.ToString() ?? "Generator"));
    }
}
