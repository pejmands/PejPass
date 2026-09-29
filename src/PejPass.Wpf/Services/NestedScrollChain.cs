using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf.Services;

public static class NestedScrollChain
{
    private const double Epsilon = 1.0;
    private const double PixelsPerNotch = 48.0;
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

    private static readonly DependencyProperty IsHorizontalAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsHorizontalAttached",
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

    public static void AttachHorizontal(ScrollViewer inner, ScrollViewer? parentOverride = null)
    {
        if (inner is null) return;

        inner.SetValue(ParentOverrideProperty, parentOverride);

        if (inner.GetValue(IsHorizontalAttachedProperty) is true)
            return;

        inner.SetValue(IsHorizontalAttachedProperty, true);
        inner.AddHandler(
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnHorizontalPreviewMouseWheel),
            handledEventsToo: true);

        if (inner.Background is null)
            inner.Background = Brushes.Transparent;
    }

    public static void Attach(PasswordBox passwordBox, ScrollViewer? parentOverride = null)
    {
        if (passwordBox is null) return;

        void TryAttach()
        {
            passwordBox.ApplyTemplate();
            var inner = FindDescendantScrollViewer(passwordBox);
            if (inner is not null)
            {
                Attach(inner, parentOverride);
                return;
            }

            passwordBox.Dispatcher.BeginInvoke(() =>
            {
                passwordBox.ApplyTemplate();
                var sv = FindDescendantScrollViewer(passwordBox);
                if (sv is not null)
                    Attach(sv, parentOverride);
            }, DispatcherPriority.Loaded);
        }

        if (passwordBox.IsLoaded)
            TryAttach();
        else
            passwordBox.Loaded += (_, _) => TryAttach();
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

        var offsetDelta = -(e.Delta / NotchUnit) * PixelsPerNotch;
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

    private static void OnHorizontalPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer inner)
            return;

        var parent = inner.GetValue(ParentOverrideProperty) as ScrollViewer
                     ?? FindAncestorScrollViewer(inner);

        if (inner.ScrollableWidth <= Epsilon)
        {
            if (TryScrollParent(parent, e.Delta))
                e.Handled = true;
            return;
        }

        var offsetDelta = -(e.Delta / NotchUnit) * PixelsPerNotch;
        var atStart = inner.HorizontalOffset <= Epsilon;
        var atEnd = inner.HorizontalOffset >= inner.ScrollableWidth - Epsilon;

        if (offsetDelta < 0 && atStart)
        {
            if (TryScrollParent(parent, e.Delta))
                e.Handled = true;
            return;
        }

        if (offsetDelta > 0 && atEnd)
        {
            if (TryScrollParent(parent, e.Delta))
                e.Handled = true;
            return;
        }

        inner.ScrollToHorizontalOffset(
            Math.Clamp(
                inner.HorizontalOffset + offsetDelta,
                0,
                inner.ScrollableWidth));

        e.Handled = true;
    }

    private static bool TryScrollParent(ScrollViewer? parent, int delta)
    {
        if (parent is null || parent.ScrollableHeight <= Epsilon)
            return false;

        ScrollBy(parent, -(delta / NotchUnit) * PixelsPerNotch);
        return true;
    }

    private static void ScrollBy(ScrollViewer? sv, double offsetDelta)
    {
        if (sv is null || sv.ScrollableHeight <= Epsilon)
            return;

        sv.ScrollToVerticalOffset(
            Math.Clamp(
                sv.VerticalOffset + offsetDelta,
                0,
                sv.ScrollableHeight));
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