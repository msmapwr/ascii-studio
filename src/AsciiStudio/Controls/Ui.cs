using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AsciiStudio.Controls;

public static class Ui
{
    public static StackPanel Stack(double spacing = 12) => new() { Spacing = spacing };
    public static TextBlock Text(string text, double size = 14, bool secondary = false) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources[secondary ? "StudioSecondaryText" : "StudioText"]
    };
    public static StackPanel Heading(string title, string description)
    {
        var p = Stack(6); var t = Text(title, 30); t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        p.Children.Add(t); return p;
    }
    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Child = child,
        Padding = padding ?? new Thickness(20),
        CornerRadius = new CornerRadius(12),
        Style = (Style)Application.Current.Resources["StudioCard"],
        BorderThickness = new Thickness(1)
    };
    public static Button Button(string label, Action action, bool accent = false)
    {
        var b = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(b, Id("Button", label));
        if (accent) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        b.Click += (_, _) => action(); return b;
    }
    public static Button AsyncButton(string label, Func<Task> action, bool accent = false)
    {
        var b = Button(label, () => { }, accent);
        b.Click += async (_, _) => { b.IsEnabled = false; try { await App.Window.Guard(action); } finally { b.IsEnabled = true; } };
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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(control, Id("Field", label));
        if (control is NumberBox number)
        {
            number.Loaded += (_, _) =>
            {
                if (FindDescendant<TextBox>(number) is not { } input) return;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(input,
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(number) + "Input");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, label);
            };
        }
        var p = Stack(6);
        if (control is Slider slider)
        {
            var header = new Grid();
            header.Children.Add(Text(label, 12, true));
            var value = Text(slider.Value.ToString("0.##"), 12, true);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.Loaded += (_, _) => Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(value,
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(slider) + "Value");
            slider.ValueChanged += (_, _) => value.Text = slider.Value.ToString("0.##");
            header.Children.Add(value); p.Children.Add(header);
        }
        else p.Children.Add(Text(label, 12, true));
        p.Children.Add(control); return p;
    }
    public static Slider Slider(double min, double max, double value, double step = 1) => new Slider()
    { Minimum = min, Maximum = max, Value = value, StepFrequency = step, IsThumbToolTipEnabled = true };
    private static string Id(string prefix, string label) => prefix + "_" + new string(label.Where(char.IsLetterOrDigit).ToArray());
    public static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
    public static Grid SettingsGrid(params FrameworkElement[] fields)
    {
        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 12 };
        foreach (var field in fields) grid.Children.Add(field);
        var previousColumns = 0;
        void Arrange(int columns)
        {
            if (columns == previousColumns) return;
            previousColumns = columns;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < fields.Length; i++)
            {
                if (i % columns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Grid.SetRow(fields[i], i / columns); Grid.SetColumn(fields[i], i % columns);
            }
        }
        Arrange(2);
        grid.SizeChanged += (_, _) => Arrange(grid.ActualWidth < 410 ? 1 : 2);
        return grid;
    }

    public static Flyout AdaptiveFlyout(FrameworkElement content, double preferredWidth = 420)
    {
        if (double.IsFinite(content.Width)) preferredWidth = content.Width;
        content.Width = double.NaN;
        var scroll = content as ScrollViewer ?? new ScrollViewer { Content = content, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var flyout = new Flyout { Content = scroll, ShowMode = Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowMode.Standard };
        XamlRoot? activeRoot = null;
        void Resize()
        {
            if (activeRoot is null) return;
            scroll.Width = Math.Max(1, Math.Min(preferredWidth, activeRoot.Size.Width - 64));
            scroll.MaxHeight = Math.Max(80, activeRoot.Size.Height - 160);
        }
        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
        flyout.Opened += (_, _) =>
        {
            activeRoot = scroll.XamlRoot;
            if (activeRoot is not null) activeRoot.Changed += RootChanged;
            Resize();
        };
        flyout.Opening += (_, _) =>
        {
            activeRoot = (App.Window.Content as FrameworkElement)?.XamlRoot;
            Resize();
        };
        flyout.Closed += (_, _) =>
        {
            if (activeRoot is not null) activeRoot.Changed -= RootChanged;
            activeRoot = null;
        };
        return flyout;
    }

    public static Grid Workspace(UIElement input, FrameworkElement result)
    {
        var g = new Grid { ColumnSpacing = 20, RowSpacing = 12 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(g, "ResponsiveWorkspace");
        g.RowDefinitions.Add(new() { Height = GridLength.Auto });
        g.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var scroller = new ScrollViewer { Content = input, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(scroller, "ParameterScroll");
        var inputFlyout = AdaptiveFlyout(scroller, 320);
        // A flyout and the inline layout must never own the same element.
        inputFlyout.Content = null;
        var inputButton = new Button { Content = "输入与常用设置", Flyout = inputFlyout, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(inputButton, "WorkspaceInputButton");
        g.Children.Add(inputButton); Grid.SetRow(scroller, 1); g.Children.Add(scroller);
        Grid.SetColumn(result, 1); Grid.SetRow(result, 1); g.Children.Add(result);
        var narrow = false;
        void Reflow()
        {
            if (g.ActualWidth <= 0) return;
            var next = g.ActualWidth < 760 || g.ActualHeight < 420;
            if (next == narrow) return;
            narrow = next;
            inputFlyout.Hide();
            if (narrow)
            {
                g.Children.Remove(scroller); inputFlyout.Content = scroller;
                g.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                g.ColumnDefinitions[1].Width = new GridLength(0);
                Grid.SetColumn(result, 0); inputButton.Visibility = Visibility.Visible;
            }
            else
            {
                inputFlyout.Content = null; scroller.Width = double.NaN; scroller.MaxHeight = double.PositiveInfinity;
                g.Children.Add(scroller);
                g.ColumnDefinitions[0].Width = new GridLength(280);
                g.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(result, 1); inputButton.Visibility = Visibility.Collapsed;
            }
        }
        g.SizeChanged += (_, _) => Reflow();
        return g;
    }

    public static Grid ResponsiveCards(params FrameworkElement[] cards)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        foreach (var card in cards) grid.Children.Add(card);
        var previous = 0;
        void Reflow()
        {
            var count = grid.ActualWidth >= 840 ? 3 : grid.ActualWidth >= 560 ? 2 : 1;
            if (count == previous) return;
            previous = count; grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var c = 0; c < count; c++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < cards.Length; i++)
            {
                if (i % count == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Grid.SetRow(cards[i], i / count); Grid.SetColumn(cards[i], i % count);
            }
        }
        grid.SizeChanged += (_, _) => Reflow(); Reflow(); return grid;
    }
    public static Grid Page(UIElement heading, FrameworkElement content)
    {
        var grid = new Grid { RowSpacing = 24 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(heading); Grid.SetRow(content, 1); grid.Children.Add(content);
        grid.SizeChanged += (_, _) =>
        {
            var shortWindow = grid.ActualHeight < 420;
            grid.RowSpacing = shortWindow ? 12 : 24;
            if (heading is StackPanel title && title.Children.Count >= 1)
            {
                if (title.Children[0] is TextBlock text) text.FontSize = grid.ActualWidth < 640 ? 24 : 30;
                if (title.Children.Count >= 2) title.Children[1].Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
            }
        };
        return grid;
    }
}
