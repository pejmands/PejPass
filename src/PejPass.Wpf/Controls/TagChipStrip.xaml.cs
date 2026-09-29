using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Controls;

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
        NestedScrollChain.AttachHorizontal(
            Scroller,
            FindAncestorScrollViewer(this));
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