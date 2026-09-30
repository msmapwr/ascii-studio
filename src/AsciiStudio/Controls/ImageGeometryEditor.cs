using AsciiStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace AsciiStudio.Controls;

public sealed class ImageGeometryEditor : Grid
{
    private readonly ImageGeometryHistory history = new();
    private readonly ContentControl host = new() { IsEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly NumberBox left = Percent(0), top = Percent(0), width = Percent(100), height = Percent(100);
    private readonly TextBlock orientation = Ui.Text("0°", 16);
    private readonly CheckBox horizontal = new() { Content = "水平翻转" }, vertical = new() { Content = "垂直翻转" };
    private readonly Button undo, redo;
    private bool refreshing;
    public ImageGeometry Current => history.Current;
    public bool SourceAvailable { set => host.IsEnabled = value; }
    public event Action? Changed;

    public ImageGeometryEditor()
    {
        var content = Ui.Stack(12);
        content.Children.Add(Ui.Text("裁剪 · 旋转前的原图百分比", 16));
        content.Children.Add(Ui.SettingsGrid(Field("左侧 %", "CropLeft", left), Field("顶部 %", "CropTop", top),
            Field("宽度 %", "CropWidth", width), Field("高度 %", "CropHeight", height)));
        content.Children.Add(Ui.AsyncButton("应用裁剪", () =>
        {
            var geometry = Current with { Left = Read(left), Top = Read(top), Width = Read(width), Height = Read(height) };
            Apply(geometry); return Task.CompletedTask;
        }));
        AutomationProperties.SetAutomationId(content.Children[^1], "ImageCropApply");
        AutomationProperties.SetAutomationId(orientation, "ImageRotationValue"); content.Children.Add(orientation);
        content.Children.Add(Ui.SettingsGrid(Action("左转 90°", "ImageRotateLeft", () => Apply(Current with { QuarterTurns = (Current.QuarterTurns + 3) % 4 })),
            Action("右转 90°", "ImageRotateRight", () => Apply(Current with { QuarterTurns = (Current.QuarterTurns + 1) % 4 }))));
        content.Children.Add(Action("旋转 180°", "ImageRotateHalf", () => Apply(Current with { QuarterTurns = (Current.QuarterTurns + 2) % 4 })));
        AutomationProperties.SetAutomationId(horizontal, "ImageFlipHorizontal");
        AutomationProperties.SetAutomationId(vertical, "ImageFlipVertical");
        content.Children.Add(Ui.SettingsGrid(horizontal, vertical));
        horizontal.Checked += (_, _) => Flip(); horizontal.Unchecked += (_, _) => Flip();
        vertical.Checked += (_, _) => Flip(); vertical.Unchecked += (_, _) => Flip();
        undo = Action("撤销", "ImageGeometryUndo", () => { history.Undo(); Notify(); });
        redo = Action("重做", "ImageGeometryRedo", () => { history.Redo(); Notify(); });
        content.Children.Add(Ui.SettingsGrid(undo, redo));
        content.Children.Add(Action("重置裁剪与方向", "ImageGeometryReset", () => Apply(new())));
        host.Content = content; Children.Add(host); Refresh();
    }

    private static NumberBox Percent(double value) => new() { Minimum = 0, Maximum = 100, Value = value, SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private static FrameworkElement Field(string label, string id, NumberBox number)
    {
        var field = Ui.Field(label, number); AutomationProperties.SetAutomationId(number, id); return field;
    }
    private static Button Action(string label, string id, Action action)
    {
        var button = Ui.Button(label, action); AutomationProperties.SetAutomationId(button, id); return button;
    }
    private static double Read(NumberBox number)
    {
        // Read the visible edit buffer as well, so applying via keyboard/UIA
        // does not depend on a previous focus loss to commit NumberBox.Value.
        var text = Ui.FindDescendant<TextBox>(number)?.Text ?? number.Text;
        if (!double.TryParse(text, System.Globalization.CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value))
            throw new ArgumentException("请填写有效的裁剪百分比。");
        return value;
    }
    private void Flip()
    {
        if (!refreshing) Apply(Current with { FlipHorizontal = horizontal.IsChecked == true, FlipVertical = vertical.IsChecked == true });
    }
    private void Apply(ImageGeometry geometry)
    {
        var previous = Current; history.Apply(geometry); Refresh();
        if (previous != Current) Changed?.Invoke();
    }
    private void Notify() { Refresh(); Changed?.Invoke(); }
    private void Refresh()
    {
        refreshing = true;
        try
        {
            var value = Current; left.Value = value.Left; top.Value = value.Top; width.Value = value.Width; height.Value = value.Height;
            horizontal.IsChecked = value.FlipHorizontal; vertical.IsChecked = value.FlipVertical;
            orientation.Text = $"旋转 {value.QuarterTurns * 90}°";
            undo.IsEnabled = history.CanUndo; redo.IsEnabled = history.CanRedo;
        }
        finally { refreshing = false; }
    }
    public void Load(ImageGeometry? geometry = null) { history.Clear(geometry); Refresh(); }
}
