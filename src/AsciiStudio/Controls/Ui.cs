using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AsciiStudio.Controls;

public static class Ui
{
    public static StackPanel Stack(double spacing = 12) => new() { Spacing = spacing };
    public static TextBlock Text(string text, double size = 14, bool secondary = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources[secondary ? "StudioSecondaryText" : "StudioText"]
    };
    public static StackPanel Heading(string title, string description)
    {
        var p = Stack(6); var t = Text(title, 30); t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        p.Children.Add(t); p.Children.Add(Text(description, 13, true)); return p;
    }
    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Child = child, Padding = padding ?? new Thickness(20), CornerRadius = new CornerRadius(12),
        Style = (Style)Application.Current.Resources["StudioCard"], BorderThickness = new Thickness(1)
    };
    public static Button Button(string label, Action action, bool accent = false)
    {
        var b = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (accent) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        b.Click += (_, _) => action(); return b;
    }
    public static Button AsyncButton(string label, Func<Task> action, bool accent = false)
    {
        var b=Button(label,()=>{},accent);
        b.Click+=async (_,_)=>{b.IsEnabled=false;try{await App.Window.Guard(action);}finally{b.IsEnabled=true;}};
        return b;
    }
    public static ComboBox Choice(IEnumerable<string> values, int selected = 0)
    {
        var c = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var value in values) c.Items.Add(value);
        c.SelectedIndex = selected; return c;
    }
    public static FrameworkElement Field(string label, UIElement control)
    {
        var p = Stack(6); p.Children.Add(Text(label, 12, true)); p.Children.Add(control); return p;
    }
    public static Slider Slider(double min, double max, double value, double step = 1) => new()
    { Minimum = min, Maximum = max, Value = value, StepFrequency = step, IsThumbToolTipEnabled = true };
    public static Grid Workspace(UIElement input, FrameworkElement result)
    {
        var g = new Grid { ColumnSpacing = 20 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var scroller = new ScrollViewer { Content = input, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        g.Children.Add(scroller); Grid.SetColumn(result, 1); g.Children.Add(result); return g;
    }
    public static Grid Page(UIElement heading, FrameworkElement content)
    {
        var grid = new Grid { RowSpacing = 24 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(heading); Grid.SetRow(content, 1); grid.Children.Add(content); return grid;
    }
}
