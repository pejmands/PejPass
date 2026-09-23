using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf.Services;

/// <summary>
/// Nested scroll chaining (standard desktop / web pattern):
/// 1) Wheel scrolls the inner scroller while it has room.
/// 2) At top/bottom (or if content fits), further wheel deltas scroll the parent ScrollViewer.
/// </summary>
public static class NestedScrollChain
{
    private const double Epsilon = 1.0;
    private const double PixelsPerNotch = 48.0; // ~3 lines — matches typical WPF wheel feel
    private const double NotchUnit = 120.0;

    private static readonly DependencyProperty ParentOverrideProperty =
        DependencyProperty.RegisterAttached(
            "ParentOverride",
            typeof(ScrollViewer),
            typeof(NestedScrollChain),
            new PropertyMetadata(null));

    private static readonly DependencyProperty IsAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsAttached",
            typeof(bool),
            typeof(NestedScrollChain),
            new PropertyMetadata(false));

    public static void Attach(ScrollViewer inner, ScrollViewer? parentOverride = null)
    {
        if (inner is null) return;

        inner.SetValue(ParentOverrideProperty, parentOverride);

        if (inner.GetValue(IsAttachedProperty) is true)
            return;

        inner.SetValue(IsAttachedProperty, true);
        inner.AddHandler(
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnInnerPreviewMouseWheel),
            handledEventsToo: true);

        if (inner.Background is null)
            inner.Background = Brushes.Transparent;
    }

    public static void Attach(TextBox textBox, ScrollViewer? parentOverride = null)
    {
        if (textBox is null) return;

        void TryAttach()
        {
            textBox.ApplyTemplate();
            var inner = FindDescendantScrollViewer(textBox);
            if (inner is not null)
            {
                Attach(inner, parentOverride);
                return;
            }

            textBox.Dispatcher.BeginInvoke(() =>
            {
                textBox.ApplyTemplate();
                var sv = FindDescendantScrollViewer(textBox);
                if (sv is not null)
                    Attach(sv, parentOverride);
            }, DispatcherPriority.Loaded);
        }

        if (textBox.IsLoaded)
            TryAttach();
        else
            textBox.Loaded += (_, _) => TryAttach();
    }

    private static void OnInnerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer inner)
            return;

        e.Handled = true;

        // e.Delta > 0 → scroll up → decrease offset; e.Delta < 0 → scroll down → increase offset
        var notches = e.Delta / NotchUnit;
        var offsetDelta = -notches * PixelsPerNotch;

        var parent = inner.GetValue(ParentOverrideProperty) as ScrollViewer
                     ?? FindAncestorScrollViewer(inner);

        if (inner.ScrollableHeight <= Epsilon)
        {
            ScrollBy(parent, offsetDelta);
            return;
        }

        var atTop = inner.VerticalOffset <= Epsilon;
        var atBottom = inner.VerticalOffset >= inner.ScrollableHeight - Epsilon;

        if (offsetDelta > 0 && atBottom)
        {
            ScrollBy(parent, offsetDelta);
            return;
        }

        if (offsetDelta < 0 && atTop)
        {
            ScrollBy(parent, offsetDelta);
            return;
        }

        ScrollBy(inner, offsetDelta);
    }

    private static void ScrollBy(ScrollViewer? sv, double offsetDelta)
    {
        if (sv is null) return;
        if (sv.ScrollableHeight <= Epsilon) return;

        var target = sv.VerticalOffset + offsetDelta;
        if (target < 0) target = 0;
        if (target > sv.ScrollableHeight) target = sv.ScrollableHeight;
        sv.ScrollToVerticalOffset(target);
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject child)
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is ScrollViewer sv)
                return sv;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer direct)
            return direct;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindDescendantScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }

        return null;
    }
}
