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
///
/// Fully handles PreviewMouseWheel so WPF cannot "eat" the event at a non-scrollable boundary.
/// </summary>
public static class NestedScrollChain
{
    private const double Epsilon = 1.0;

    public static void Attach(ScrollViewer inner)
    {
        if (inner is null) return;

        inner.PreviewMouseWheel -= OnInnerPreviewMouseWheel;
        inner.PreviewMouseWheel += OnInnerPreviewMouseWheel;
    }

    /// <summary>
    /// Attach to a multiline TextBox after its template (internal ScrollViewer) exists.
    /// </summary>
    public static void Attach(TextBox textBox)
    {
        if (textBox is null) return;

        void TryAttach()
        {
            textBox.ApplyTemplate();
            var inner = FindDescendantScrollViewer(textBox);
            if (inner is not null)
            {
                Attach(inner);
                return;
            }

            textBox.Dispatcher.BeginInvoke(() =>
            {
                textBox.ApplyTemplate();
                var sv = FindDescendantScrollViewer(textBox);
                if (sv is not null)
                    Attach(sv);
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

        // Always handle so WPF ScrollViewer cannot swallow the event at a boundary.
        e.Handled = true;

        var delta = e.Delta;
        var parent = FindAncestorScrollViewer(inner);

        if (inner.ScrollableHeight <= Epsilon)
        {
            ScrollBy(parent, delta);
            return;
        }

        var scrollingDown = delta < 0;
        var atTop = inner.VerticalOffset <= Epsilon;
        var atBottom = inner.VerticalOffset >= inner.ScrollableHeight - Epsilon;

        if (scrollingDown && atBottom)
        {
            ScrollBy(parent, delta);
            return;
        }

        if (!scrollingDown && atTop)
        {
            ScrollBy(parent, delta);
            return;
        }

        ScrollBy(inner, delta);
    }

    private static void ScrollBy(ScrollViewer? sv, double wheelDelta)
    {
        if (sv is null) return;
        if (sv.ScrollableHeight <= Epsilon) return;

        var target = sv.VerticalOffset - wheelDelta;
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
