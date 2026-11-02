using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Input;
using System.Windows.Threading;

namespace PejPass.Wpf.Views;

public partial class TrashWindow : Window
{
    public TrashWindow(TrashViewModel viewModel)
    {
        InitializeComponent();
        App.PrepareCustomChrome(this);

        DataContext = viewModel;
        KeyDown += OnKeyDown;
        TrashList.SelectionChanged += (_, _) => UpdateSelectedTrashToolbar();

        FaviconService.FaviconsBatchReady += OnFaviconsReady;
        Loaded += (_, _) =>
            FaviconService.Prefetch(viewModel.Items.Select(i => (i.Url, i.Title)));
        Closed += (_, _) => FaviconService.FaviconsBatchReady -= OnFaviconsReady;
    }

    private void TrashListBoxItem_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item ||
            FindVisualAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        item.Focus();
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
                return match;

            element = element switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(element),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(element)
            };
        }

        return null;
    }

    private void TrashWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab ||
            Keyboard.FocusedElement is not UIElement focusedElement)
        {
            return;
        }

        var reverse = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;
        if (TryNavigateTrashTab(focusedElement, reverse))
            e.Handled = true;
    }

    private bool TryNavigateTrashTab(UIElement focusedElement, bool reverse)
    {
        var container = ItemsControl.ContainerFromElement(
            TrashList,
            focusedElement) as ListBoxItem;

        if (container is not null)
        {
            var currentIndex = TrashList.ItemContainerGenerator.IndexFromContainer(container);
            if (currentIndex < 0)
                return false;

            var tabStops = GetTrashItemTabStops(container);
            var focusedIndex = tabStops.FindIndex(
                element => ReferenceEquals(element, focusedElement));
            if (focusedIndex < 0)
                return false;

            var nextFocusIndex = focusedIndex + (reverse ? -1 : 1);
            if (nextFocusIndex >= 0 && nextFocusIndex < tabStops.Count)
            {
                FocusTrashTabStop(tabStops[nextFocusIndex]);
                return true;
            }

            if (!reverse)
            {
                if (currentIndex < TrashList.Items.Count - 1)
                    FocusTrashItem(currentIndex + 1, 0);
                else
                    FocusFirstFooterTabStop();

                return true;
            }

            if (currentIndex > 0)
                FocusTrashItem(currentIndex - 1, -1);
            else
                FocusLastSearchTabStop();

            return true;
        }

        if (!reverse &&
            FindVisualAncestor<SearchBox>(focusedElement) is not null &&
            IsLastSearchTabStop(focusedElement) &&
            TrashList.Items.Count > 0)
        {
            FocusTrashItem(0, 0);
            return true;
        }

        if (reverse &&
            TrashList.Items.Count > 0 &&
            ReferenceEquals(GetFirstFooterTabStop(), focusedElement))
        {
            FocusTrashItem(TrashList.Items.Count - 1, -1);
            return true;
        }

        return false;
    }

    private bool IsLastSearchTabStop(UIElement focusedElement)
    {
        var tabStops = FindVisualChildren<UIElement>(TrashSearchBox)
            .Where(IsKeyboardTabStop)
            .ToList();

        return tabStops.Count > 0 &&
            ReferenceEquals(tabStops[^1], focusedElement);
    }

    private void FocusTrashItem(int index, int tabStopIndex)
    {
        if (index < 0 || index >= TrashList.Items.Count)
            return;

        TrashList.ScrollIntoView(TrashList.Items[index]);
        TrashList.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                TrashList.UpdateLayout();

                if (TrashList.ItemContainerGenerator.ContainerFromIndex(index)
                    is not ListBoxItem container)
                {
                    return;
                }

                var tabStops = GetTrashItemTabStops(container);
                var targetIndex = tabStopIndex < 0
                    ? tabStops.Count - 1
                    : tabStopIndex;

                if (targetIndex >= 0 && targetIndex < tabStops.Count)
                    FocusTrashTabStop(tabStops[targetIndex]);
            }));
    }

    private void FocusTrashTabStop(UIElement target)
    {
        var selectedItems = TrashList.SelectedItems.Cast<object>().ToArray();

        target.Focus();

        var currentSelection = TrashList.SelectedItems.Cast<object>().ToArray();
        if (selectedItems.SequenceEqual(currentSelection))
            return;

        TrashList.SelectedItems.Clear();
        foreach (var selectedItem in selectedItems)
            TrashList.SelectedItems.Add(selectedItem);
    }

    private void FocusLastSearchTabStop()
    {
        FindVisualChildren<UIElement>(TrashSearchBox)
            .Where(IsKeyboardTabStop)
            .LastOrDefault()
            ?.Focus();
    }

    private void FocusFirstFooterTabStop()
    {
        GetFooterTabStops().FirstOrDefault()?.Focus();
    }

    private UIElement? GetFirstFooterTabStop() =>
        GetFooterTabStops().FirstOrDefault();

    private IEnumerable<UIElement> GetFooterTabStops()
    {
        foreach (var button in new[]
        {
            RestoreSelectedButton,
            PurgeSelectedButton,
            CloseButton
        })
        {
            if (IsKeyboardTabStop(button))
                yield return button;
        }
    }

    private static List<UIElement> GetTrashItemTabStops(ListBoxItem container)
    {
        var tabStops = new List<UIElement>();
        if (IsKeyboardTabStop(container))
            tabStops.Add(container);

        tabStops.AddRange(
            FindVisualChildren<Button>(container)
                .Where(IsKeyboardTabStop));

        return tabStops;
    }

    private static bool IsKeyboardTabStop(UIElement element) =>
        element.IsVisible &&
        element.IsEnabled &&
        element.Focusable &&
        element is Control control &&
        control.IsTabStop;

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void OnFaviconsReady(IReadOnlyCollection<string> hosts)
    {
        if (!IsLoaded || hosts.Count == 0)
            return;

        var affectedHosts = hosts.ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var image in FindVisualChildren<Image>(this))
            {
                if (image.DataContext is not TrashRow row)
                    continue;

                var host = FaviconService.TryGetHost(row.Url);
                if (host is null || !affectedHosts.Contains(host))
                    continue;

                image.GetBindingExpression(Image.SourceProperty)?.UpdateTarget();
            }
        }
        catch (InvalidOperationException)
        {
            // The visual tree may be changing during shutdown.
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        if (parent is null)
            yield break;

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private void UpdateSelectedTrashToolbar()
    {
        var count = TrashList.SelectedItems.Count;
        SelectedTrashCountText.Text = count == 1 ? "1 selected" : $"{count} selected";
        SelectedTrashCountText.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreSelectedButton.IsEnabled = count > 0;
        PurgeSelectedButton.IsEnabled = count > 0;
    }

    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TrashViewModel vm)
            return;

        var selected = TrashList.SelectedItems.Cast<TrashRow>().ToArray();
        if (vm.RestoreSelectedCommand.CanExecute(selected))
            vm.RestoreSelectedCommand.Execute(selected);
    }

    private void PurgeSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TrashViewModel vm)
            return;

        var selected = TrashList.SelectedItems.Cast<TrashRow>().ToArray();
        if (vm.PurgeSelectedCommand.CanExecute(selected))
            vm.PurgeSelectedCommand.Execute(selected);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
