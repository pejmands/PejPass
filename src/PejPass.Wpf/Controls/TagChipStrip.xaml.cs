using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PejPass.Wpf.Controls;

/// <summary>
/// Horizontal one-row tag chip strip with mouse-wheel scrolling.
/// Bind ItemsSource to items that expose DisplayLabel + IsSelected;
/// ChipCommand receives the clicked item as CommandParameter.
/// </summary>
public partial class TagChipStrip : UserControl
{
    private const double Epsilon = 1.0;
    private const double PixelsPerNotch = 48.0;
    private const double NotchUnit = 120.0;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(TagChipStrip),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ChipCommandProperty =
        DependencyProperty.Register(
            nameof(ChipCommand),
            typeof(ICommand),
            typeof(TagChipStrip),
            new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand? ChipCommand
    {
        get => (ICommand?)GetValue(ChipCommandProperty);
        set => SetValue(ChipCommandProperty, value);
    }

    public TagChipStrip()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachWheel();
    }

    private void AttachWheel()
    {
        RemoveHandler(
            UIElement.PreviewMouseWheelEvent,
            (MouseWheelEventHandler)OnPreviewMouseWheel);

        AddHandler(
            UIElement.PreviewMouseWheelEvent,
            (MouseWheelEventHandler)OnPreviewMouseWheel,
            handledEventsToo: true);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var parent = FindAncestorScrollViewer(this);

        if (Scroller.ExtentWidth <= Scroller.ViewportWidth + Epsilon)
        {
            ScrollParent(parent, e.Delta);
            e.Handled = parent is not null;
            return;
        }

        var notches = e.Delta / NotchUnit;
        var offsetDelta = -notches * PixelsPerNotch;
        var atStart = Scroller.HorizontalOffset <= Epsilon;
        var atEnd = Scroller.HorizontalOffset >= Scroller.ScrollableWidth - Epsilon;

        if (offsetDelta < 0 && atEnd)
        {
            ScrollParent(parent, e.Delta);
            e.Handled = parent is not null;
            return;
        }

        if (offsetDelta > 0 && atStart)
        {
            ScrollParent(parent, e.Delta);
            e.Handled = parent is not null;
            return;
        }

        Scroller.ScrollToHorizontalOffset(
            Math.Clamp(
                Scroller.HorizontalOffset + offsetDelta,
                0,
                Scroller.ScrollableWidth));

        e.Handled = true;
    }

    private static void ScrollParent(ScrollViewer? parent, int delta)
    {
        if (parent is null || parent.ScrollableHeight <= Epsilon)
            return;

        var notches = delta / NotchUnit;
        var offsetDelta = -notches * PixelsPerNotch;
        var target = Math.Clamp(
            parent.VerticalOffset + offsetDelta,
            0,
            parent.ScrollableHeight);

        parent.ScrollToVerticalOffset(target);
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject child)
    {
        var current = VisualTreeHelper.GetParent(child);

        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer)
                return scrollViewer;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
