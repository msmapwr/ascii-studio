using Charloom.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Charloom.Controls;

/// <summary>Refreshes presentation properties in place without recreating editing controls.</summary>
public static class UiLocalization
{
    private sealed class Label(DependencyObject target, DependencyProperty property)
    {
        public readonly WeakReference<DependencyObject> Target = new(target);
        public readonly DependencyProperty Property = property;
        public string? Source;
        public string? Display;
        public bool Updating;
        public void Apply()
        {
            if (Source is null || !Target.TryGetTarget(out var target)) return;
            Updating = true;
            try
            {
                Display = GuiText.Translate(Source);
                if (!Equals(target.GetValue(Property), Display)) target.SetValue(Property, Display);
            }
            finally { Updating = false; }
        }
    }
    // Store sources on the native element. WinRT can recreate its managed wrapper;
    // a ConditionalWeakTable keyed by that wrapper loses the original language.
    private static readonly DependencyProperty LabelsProperty = DependencyProperty.RegisterAttached(
        "LocalizationLabels", typeof(object), typeof(UiLocalization), new PropertyMetadata(null));
    private static readonly DependencyProperty VerbatimProperty = DependencyProperty.RegisterAttached(
        "Verbatim", typeof(bool), typeof(UiLocalization), new PropertyMetadata(false));
    private static readonly List<WeakReference<Label>> labels = [];

    private static Dictionary<DependencyProperty, Label?> Labels(DependencyObject target)
    {
        if (target.GetValue(LabelsProperty) is Dictionary<DependencyProperty, Label?> existing) return existing;
        var created = new Dictionary<DependencyProperty, Label?>(); target.SetValue(LabelsProperty, created); return created;
    }
    public static T Verbatim<T>(T element) where T : DependencyObject
    { element.SetValue(VerbatimProperty, true); return element; }

    public static void Preserve(DependencyObject target, DependencyProperty property) => Labels(target)[property] = null;

    public static void Watch(DependencyObject target, DependencyProperty property)
    {
        var sources = Labels(target);
        if (sources.TryGetValue(property, out var existing))
        { existing?.Target.SetTarget(target); existing?.Apply(); return; }
        var label = new Label(target, property); sources.Add(property, label);
        void Changed(DependencyObject sender, DependencyProperty changed)
        {
            if (label.Updating) return;
            if (sender.GetValue(changed) is not string source) { label.Source = null; return; }
            if (source == label.Display) return;
            label.Source = source;
            label.Apply();
        }
        target.RegisterPropertyChangedCallback(property, Changed);
        labels.Add(new(label));
        Changed(target, property);
    }

    public static void Refresh()
    {
        labels.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in labels) if (reference.TryGetTarget(out var label)) label.Apply();
    }

    public static void Attach(DependencyObject root)
    {
        if ((bool)root.GetValue(VerbatimProperty)) return;
        if (!string.IsNullOrEmpty(AutomationProperties.GetName(root))) Watch(root, AutomationProperties.NameProperty);
        Watch(root, ToolTipService.ToolTipProperty);
        switch (root)
        {
            case CommandBar bar:
                foreach (var command in bar.PrimaryCommands.Concat(bar.SecondaryCommands).OfType<DependencyObject>()) Attach(command);
                break;
            case AppBarButton button: Watch(button, AppBarButton.LabelProperty); break;
            case ComboBox choice:
                foreach (var item in choice.Items.OfType<ComboBoxItem>()) Watch(item, ComboBoxItem.ContentProperty);
                break;
            case TextBlock text: Watch(text, TextBlock.TextProperty); break;
            // Text itself is user data, including read-only samples and output.
            case TextBox text: Watch(text, TextBox.PlaceholderTextProperty); break;
            case AutoSuggestBox search: Watch(search, AutoSuggestBox.PlaceholderTextProperty); break;
            case ToggleSwitch toggle:
                Watch(toggle, ToggleSwitch.HeaderProperty);
                toggle.OnContent = "开"; toggle.OffContent = "关";
                Watch(toggle, ToggleSwitch.OnContentProperty); Watch(toggle, ToggleSwitch.OffContentProperty); break;
            case NumberBox number: Watch(number, NumberBox.HeaderProperty); break;
            case Expander expander: Watch(expander, Expander.HeaderProperty); break;
            case InfoBar info:
                Watch(info, InfoBar.TitleProperty); Watch(info, InfoBar.MessageProperty); break;
            case ContentDialog dialog:
                Watch(dialog, ContentDialog.TitleProperty); Watch(dialog, ContentDialog.ContentProperty);
                Watch(dialog, ContentDialog.PrimaryButtonTextProperty); Watch(dialog, ContentDialog.SecondaryButtonTextProperty);
                Watch(dialog, ContentDialog.CloseButtonTextProperty); break;
            case ContentControl content when root is not TabViewItem:
                Watch(content, ContentControl.ContentProperty); break;
        }
        // Do not visit generated control templates: TextBox text, selected font names,
        // project headers and artwork previews must keep their exact source content.
        if (root is TextBox or AutoSuggestBox or ComboBox or NumberBox or ToggleSwitch or TabViewItem) return;
        if (root is ContentControl control && control.Content is DependencyObject child) Attach(child);
        if (root is Expander expanderContent && expanderContent.Content is DependencyObject expanded) Attach(expanded);
        if (root is InfoBar infoContent && infoContent.Content is DependencyObject body) Attach(body);
        if (root is Control) return;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Attach(VisualTreeHelper.GetChild(root, i));
    }
}
