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
        if (d is not TextBox textBox)
            return;

        if ((bool)e.NewValue)
        {
            textBox.Loaded += OnLoaded;
            textBox.Unloaded += OnUnloaded;

            if (textBox.IsLoaded)
                Attach(textBox);
        }
        else
        {
            textBox.Loaded -= OnLoaded;
            textBox.Unloaded -= OnUnloaded;
            Detach(textBox);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            Attach(textBox);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            Detach(textBox);
    }

    private static void Attach(TextBox textBox)
    {
        textBox.ApplyTemplate();

        var scrollViewer = FindScrollViewer(textBox);
        if (scrollViewer is null)
        {
            textBox.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                () => Attach(textBox));
            return;
        }

        SetScrollViewer(textBox, scrollViewer);
        textBox.PreviewMouseMove -= OnPreviewMouseMove;
        textBox.PreviewMouseMove += OnPreviewMouseMove;
    }

    private static void Detach(TextBox textBox)
    {
        textBox.PreviewMouseMove -= OnPreviewMouseMove;
        SetScrollViewer(textBox, null);
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not TextBox textBox ||
            e.LeftButton != MouseButtonState.Pressed ||
            !textBox.IsKeyboardFocusWithin)
            return;

        var scrollViewer = GetScrollViewer(textBox);
        if (scrollViewer is null || scrollViewer.ScrollableWidth <= 0)
            return;

        var point = e.GetPosition(textBox);

        if (point.X <= EdgeThreshold)
        {
            scrollViewer.ScrollToHorizontalOffset(
                Math.Max(0, scrollViewer.HorizontalOffset - ScrollStep));
        }
        else if (point.X >= textBox.ActualWidth - EdgeThreshold)
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
