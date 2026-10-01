using AsciiStudio.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace AsciiStudio.Controls;

public sealed class FontPicker : StackPanel
{
    private readonly AutoSuggestBox search = new() { QueryIcon = new SymbolIcon(Symbol.Find), PlaceholderText = "搜索字体", MaxSuggestionListHeight = 260 };
    private readonly ComboBox filter = Ui.Choice(["全部字体", "等宽字体", "中文常用"]);
    private readonly TextBlock sample = Ui.Text("abc · 测试", 18);
    private string[] monospace = [];
    public string SelectedFont { get; private set; } = "Consolas";
    public event Action<string>? Changed;
    public FontPicker(string id, bool preferMonospace = false)
    {
        Spacing = 8; Children.Add(filter); Children.Add(search); Children.Add(sample);
        AutomationProperties.SetAutomationId(search, id); AutomationProperties.SetAutomationId(filter, id + "Filter");
        void IdentifyInput()
        {
            search.ApplyTemplate();
            if (Ui.FindDescendant<TextBox>(search) is { } input) AutomationProperties.SetAutomationId(input, id + "Input");
        }
        search.Loaded += (_, _) => IdentifyInput();
        search.SizeChanged += (_, _) => IdentifyInput();
        search.TextChanged += (_, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            Suggestions();
            if (FontCatalog.Names.Contains(search.Text, StringComparer.OrdinalIgnoreCase)) Select(search.Text);
        };
        search.GotFocus += (_, _) => Suggestions();
        search.SuggestionChosen += (_, args) => Select(args.SelectedItem?.ToString() ?? SelectedFont);
        search.QuerySubmitted += (_, args) =>
        {
            var value = args.ChosenSuggestion?.ToString() ?? FontCatalog.Names.FirstOrDefault(n => n.Equals(args.QueryText, StringComparison.OrdinalIgnoreCase));
            if (value is not null) Select(value); else search.Text = SelectedFont;
        };
        search.LostFocus += (_, _) => search.Text = SelectedFont;
        filter.SelectionChanged += (_, _) => Suggestions();
        Loaded += async (_, _) =>
        {
            if (monospace.Length == 0) monospace = await Task.Run(() => FontCatalog.Names.Where(FontCatalog.IsMonospaced).ToArray());
            Suggestions();
        };
        if (preferMonospace) filter.SelectedIndex = 1;
        Select("Consolas");
    }
    private void Suggestions()
    {
        var names = filter.SelectedIndex switch { 1 => monospace, 2 => FontCatalog.Names.Where(FontCatalog.IsChineseCommon), _ => FontCatalog.Names };
        search.ItemsSource = names.Where(n => n.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public void Select(string name)
    {
        if (!FontCatalog.Names.Contains(name, StringComparer.OrdinalIgnoreCase)) name = "Consolas";
        var changed = name != SelectedFont; SelectedFont = name; search.Text = name;
        sample.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(name);
        if (changed) Changed?.Invoke(name);
    }
}
