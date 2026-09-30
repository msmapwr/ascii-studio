using AsciiStudio.Controls;
using AsciiStudio.Core;
using Microsoft.UI.Xaml.Controls;

namespace AsciiStudio.Pages;

public sealed class ToolsPage : Grid
{
    private readonly ResultPane result = new();
    private readonly TextBox input = new() { AcceptsReturn = true, MinHeight = 220, MaxLength = 100000, Text = "Hello, ASCII!", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };
    private readonly ComboBox operation = Ui.Choice(["字符分析与 ASCII 校验", "裁掉外围空白", "移除不可见控制字符", "仅保留 ASCII", "转为大写", "转为小写", "逐行左右反转", "上下反转", "Tab 转为 4 个空格"]);
    public ToolsPage()
    {
        var p = Ui.Stack(); p.Children.Add(Ui.Field("输入文字 / 字符画", input)); p.Children.Add(Ui.Field("处理方式", operation));
        p.Children.Add(Ui.AsyncButton("处理", Process, true));
        Children.Add(Ui.Page(Ui.Heading("文本工具", "检查字符、整理空白，或变换文字。"), Ui.Workspace(Ui.Card(p), result)));
    }
    private async Task Process()
    {
        var text = input.Text; var normalized = TextUtilities.Normalize(text);
        var output = operation.SelectedIndex switch
        {
            0 => TextUtilities.Analyze(text),
            1 => TextUtilities.TrimCanvas(text),
            2 => TextUtilities.Clean(text, false),
            3 => TextUtilities.Clean(text, true),
            4 => text.ToUpperInvariant(),
            5 => text.ToLowerInvariant(),
            6 => string.Join('\n', normalized.Split('\n').Select(Reverse)),
            7 => string.Join('\n', normalized.Split('\n').Reverse()),
            _ => normalized.Replace("\t", "    ")
        };
        await result.SetDocument(AsciiDocument.FromText(output, "Text tools"));
    }
    private static string Reverse(string text)
    {
        var elements = new List<string>(); var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) elements.Add(e.GetTextElement()); elements.Reverse(); return string.Concat(elements);
    }
}
