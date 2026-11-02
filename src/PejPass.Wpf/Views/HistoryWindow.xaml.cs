using PejPass.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow(HistoryViewModel viewModel)
    {
        InitializeComponent();
        App.PrepareCustomChrome(this);

        DataContext = viewModel;
        KeyDown += OnKeyDown;
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        KeyDown -= OnKeyDown;

        if (DataContext is HistoryViewModel viewModel)
            viewModel.Dispose();

        DataContext = null;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void DeleteOlderHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
            return;

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private void HistoryListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item)
            return;

        item.IsSelected = true;
        FindVisualChildren<Expander>(item).FirstOrDefault()?.Focus();
    }

    private void HistoryListBoxItem_GotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (sender is ListBoxItem item)
            item.IsSelected = true;
    }

    private void HistoryExpander_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || sender is not Expander expander)
            return;

        expander.IsExpanded = !expander.IsExpanded;
        e.Handled = true;
    }

    private void HistorySearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab ||
            (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0 ||
            HistoryItemsListBox.Items.Count == 0)
        {
            return;
        }

        var tabStops = FindVisualChildren<UIElement>(HistorySearchBox)
            .Where(IsKeyboardTabStop)
            .ToList();
        var focusedIndex = tabStops.FindIndex(
            element => ReferenceEquals(element, Keyboard.FocusedElement));

        if (focusedIndex < 0 || focusedIndex < tabStops.Count - 1)
            return;

        e.Handled = true;
        FocusHistoryItem(0);
    }

    private void HistoryItemsListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab ||
            Keyboard.FocusedElement is not DependencyObject focusedElement)
        {
            return;
        }


        if (ItemsControl.ContainerFromElement(
            HistoryItemsListBox,
            focusedElement) is not ListBoxItem container)
            return;

        var currentIndex = HistoryItemsListBox.ItemContainerGenerator
            .IndexFromContainer(container);
        if (currentIndex < 0)
            return;

        var direction = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0
            ? -1
            : 1;
        var nextIndex = currentIndex + direction;

        if (nextIndex < 0 || nextIndex >= HistoryItemsListBox.Items.Count)
            return;

        e.Handled = true;
        FocusHistoryItem(nextIndex);
    }

    private void ComparisonScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab ||
            (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) == 0 ||
            HistoryItemsListBox.Items.Count == 0)
        {
            return;
        }

        var firstTabStop = FindVisualChildren<UIElement>(ComparisonFieldsItemsControl)
            .FirstOrDefault(IsKeyboardTabStop);

        if (!ReferenceEquals(firstTabStop, Keyboard.FocusedElement))
            return;

        e.Handled = true;
        FocusHistoryItem(HistoryItemsListBox.Items.Count - 1);
    }

    private void FocusHistoryItem(int index)
    {
        if (index < 0 || index >= HistoryItemsListBox.Items.Count)
            return;

        HistoryItemsListBox.ScrollIntoView(HistoryItemsListBox.Items[index]);
        HistoryItemsListBox.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                HistoryItemsListBox.UpdateLayout();

                if (HistoryItemsListBox.ItemContainerGenerator.ContainerFromIndex(index)
                    is not ListBoxItem container)
                {
                    return;
                }

                var expander = FindVisualChildren<Expander>(container).FirstOrDefault();
                expander?.Focus();
            }));
    }

    private static bool IsKeyboardTabStop(UIElement element) =>
        element.IsVisible &&
        element.IsEnabled &&
        element.Focusable &&
        element is Control control &&
        control.IsTabStop;

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
