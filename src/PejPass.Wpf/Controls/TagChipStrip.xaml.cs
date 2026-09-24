using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PejPass.Wpf.Controls;

/// <summary>
/// Horizontal one-row tag chip strip with mouse-wheel scrolling.
/// Bind ItemsSource to items that expose DisplayLabel + IsSelected;
/// ChipCommand receives the clicked item as CommandParameter.
/// </summary>
public partial class TagChipStrip : UserControl
{
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
        Scroller.RemoveHandler(UIElement.PreviewMouseWheelEvent,
            (MouseWheelEventHandler)OnPreviewMouseWheel);
        Scroller.AddHandler(UIElement.PreviewMouseWheelEvent,
            (MouseWheelEventHandler)OnPreviewMouseWheel,
            handledEventsToo: true);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Scroller.ExtentWidth <= Scroller.ViewportWidth + 0.5)
            return;

        var notches = e.Delta / 120.0;
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - notches * 48.0);
        e.Handled = true;
    }
}
