using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PejPass.Wpf.Services;

/// <summary>
/// Standard nested-scroll chaining (same idea as modern web / mobile):
/// mouse wheel scrolls the inner scroller first; when it hits the top or bottom,
/// further wheel deltas are applied to the nearest parent ScrollViewer.
/// </summary>
public static class NestedScrollChain
{
    private const double Epsilon = 0.5;

    /// <summary>Attach chaining to a nested ScrollViewer (e.g. Notes in the detail pane).</summary>
    public static void Attach(ScrollViewer inner)
    {
        if (inner is null) return;
        inner.PreviewMouseWheel -= OnScrollViewerPreviewMouseWheel;
        inner.PreviewMouseWheel += OnScrollViewerPreviewMouseWheel;
    }

    /// <summary>
    /// Attach chaining to a multiline TextBox (uses its template ScrollViewer).
    /// Safe to call before the template is applied — waits for Loaded if needed.
    /// </summary>
    public static void Attach(TextBox textBox)
    {
        if (textBox is null) return;

        void TryAttach()
        {
            var inner = FindDescendantScrollViewer(textBox);
            if (inner is not null)
                Attach(inner);
        }

        if (textBox.IsLoaded)
            TryAttach();
        else
            textBox.Loaded += (_, _) => TryAttach();
    }

    private static void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer inner)
            return;

        // Nothing to scroll inside → always forward to parent
        if (inner.ScrollableHeight <= Epsilon)
        {
            if (TryScrollParent(inner, e.Delta))
                e.Handled = true;
            return;
        }

        var scrollingDown = e.Delta < 0;
        var atTop = inner.VerticalOffset <= Epsilon;
        var atBottom = inner.VerticalOffset >= inner.ScrollableHeight - Epsilon;

        // Still room to move inside → let the default ScrollViewer handle it
        if ((scrollingDown && !atBottom) || (!scrollingDown && !atTop))
            return;

        // Boundary hit → chain to parent
        if (TryScrollParent(inner, e.Delta))
            e.Handled = true;
    }

    private static bool TryScrollParent(DependencyObject from, double delta)
    {
        var parent = FindAncestorScrollViewer(from, skip: from as ScrollViewer);
        if (parent is null)
            return false;

        var target = parent.VerticalOffset - delta;
        target = Math.Clamp(target, 0, parent.ScrollableHeight);
        parent.ScrollToVerticalOffset(target);
        return true;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? child, ScrollViewer? skip)
    {
        while (child is not null)
        {
            child = VisualTreeHelper.GetParent(child);
            if (child is ScrollViewer sv && !ReferenceEquals(sv, skip))
                return sv;
        }

        return null;
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
            return sv;

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
