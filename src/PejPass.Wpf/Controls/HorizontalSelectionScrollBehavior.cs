using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf.Controls;

public static class HorizontalSelectionScrollBehavior
{
    private const double EdgeThreshold = 18;
    private const double ScrollStep = 24;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(HorizontalSelectionScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty ScrollViewerProperty =
        DependencyProperty.RegisterAttached(
            "ScrollViewer",
            typeof(ScrollViewer),
            typeof(HorizontalSelectionScrollBehavior),
            new PropertyMetadata(null));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control)
            return;

        if ((bool)e.NewValue)
        {
            control.Loaded += OnLoaded;
            control.Unloaded += OnUnloaded;

            if (control.IsLoaded)
                Attach(control);
        }
        else
        {
            control.Loaded -= OnLoaded;
            control.Unloaded -= OnUnloaded;
            Detach(control);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Control control)
            Attach(control);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Control control)
            Detach(control);
    }

    private static void Attach(Control control)
    {
        control.ApplyTemplate();

        var scrollViewer = FindScrollViewer(control);
        if (scrollViewer is null)
        {
            control.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                () => Attach(control));
            return;
        }

        SetScrollViewer(control, scrollViewer);
        control.PreviewMouseMove -= OnPreviewMouseMove;
        control.PreviewMouseMove += OnPreviewMouseMove;
    }

    private static void Detach(Control control)
    {
        control.PreviewMouseMove -= OnPreviewMouseMove;
        SetScrollViewer(control, null);
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Control control ||
            e.LeftButton != MouseButtonState.Pressed ||
            !control.IsKeyboardFocusWithin)
            return;

        var scrollViewer = GetScrollViewer(control);
        if (scrollViewer is null || scrollViewer.ScrollableWidth <= 0)
            return;

        var point = e.GetPosition(control);

        if (point.X <= EdgeThreshold)
        {
            scrollViewer.ScrollToHorizontalOffset(
                Math.Max(0, scrollViewer.HorizontalOffset - ScrollStep));
        }
        else if (point.X >= control.ActualWidth - EdgeThreshold)
        {
            scrollViewer.ScrollToHorizontalOffset(
                Math.Min(
                    scrollViewer.ScrollableWidth,
                    scrollViewer.HorizontalOffset + ScrollStep));
        }
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject element) =>
        (ScrollViewer?)element.GetValue(ScrollViewerProperty);

    private static void SetScrollViewer(DependencyObject element, ScrollViewer? value) =>
        element.SetValue(ScrollViewerProperty, value);

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scrollViewer)
                return scrollViewer;

            var nested = FindScrollViewer(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
