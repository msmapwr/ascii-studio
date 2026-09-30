using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace AsciiStudio.Controls;

internal static class ParameterWheel
{
    internal static void Attach(FrameworkElement control)
    {
        double stableValue=control switch{NumberBox n=>n.Value,Slider s=>s.Value,_=>0};
        int stableSelection=control is ComboBox c?c.SelectedIndex:-1;
        // Commit after the current input event. If a platform handler processes
        // the wheel first, we can restore the value from before that event.
        void Commit()=>control.DispatcherQueue.TryEnqueue(()=>
        {
            stableValue=control switch{NumberBox n=>n.Value,Slider s=>s.Value,_=>0};
            if(control is ComboBox c)stableSelection=c.SelectedIndex;
        });
        if(control is NumberBox number)number.ValueChanged+=(_,_)=>Commit();
        if(control is Slider slider)slider.ValueChanged+=(_,_)=>Commit();
        if(control is ComboBox combo)combo.SelectionChanged+=(_,_)=>Commit();
        control.AddHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler((_,e)=>
        {
            if(control is ComboBox{IsDropDownOpen:true})return;
            if(!ScrollParent(control,e))return;
            if(control is NumberBox n&&n.Value!=stableValue)n.Value=stableValue;
            if(control is Slider s&&s.Value!=stableValue)s.Value=stableValue;
            if(control is ComboBox c&&c.SelectedIndex!=stableSelection)c.SelectedIndex=stableSelection;
        }),true);
    }
    internal static bool ScrollParent(FrameworkElement sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender);
        if (point.Properties.IsHorizontalMouseWheel) return false;
        DependencyObject? parent = sender;
        while ((parent = VisualTreeHelper.GetParent(parent)) is not null)
        {
            if (parent is not ScrollViewer scroll) continue;
            var delta = point.Properties.MouseWheelDelta / 120d * 48;
            scroll.ChangeView(null, Math.Clamp(scroll.VerticalOffset - delta, 0, scroll.ScrollableHeight), null, true);
            e.Handled = true;
            return true;
        }
        return false;
    }
}
