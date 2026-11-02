using PejPass.Domain.Entities;
using PejPass.Wpf.Controls;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf.Tests.Windowing;

public sealed class TrashWindowKeyboardNavigationTests
{
    [Fact(Timeout = 20000)]
    public void TrashList_ExposesRowsAndActionsWithVisibleKeyboardFocus()
    {
        WpfTestHost.Run(_ =>
        {
            var vault = new Vault();
            foreach (var index in Enumerable.Range(0, 30))
            {
                var entry = new VaultEntry
                {
                    Title = $"Trash entry {index:D2}",
                    Username = $"user{index:D2}"
                };

                vault.AddEntry(entry);
                vault.SoftDelete(entry.Id);
            }

            var viewModel = new TrashViewModel(
                vault,
                () => Task.FromResult(true),
                () => Task.FromResult(true),
                () => true,
                _ => false);
            var window = new TrashWindow(viewModel);

            var searchBox = Assert.IsType<SearchBox>(window.FindName("TrashSearchBox"));
            var trashList = Assert.IsType<ListBox>(window.FindName("TrashList"));

            window.Show();
            window.UpdateLayout();

            var searchInput = Assert.Single(FindVisualChildren<TextBox>(searchBox));
            var listScrollViewer = Assert.Single(
                FindVisualChildren<ScrollViewer>(trashList));

            Assert.Equal(30, viewModel.Items.Count);
            Assert.True(viewModel.EmptyTrashCommand.CanExecute(null));
            Assert.Equal(
                KeyboardNavigationMode.Continue,
                KeyboardNavigation.GetTabNavigation(trashList));
            Assert.True(VirtualizingPanel.GetIsVirtualizing(trashList));
            Assert.Null(trashList.ItemContainerGenerator.ContainerFromIndex(20));

            Assert.False(searchBox.Focusable);
            Assert.False(searchBox.IsTabStop);
            Assert.True(searchInput.Focusable);
            Assert.True(searchInput.IsTabStop);

            Assert.False(trashList.Focusable);
            Assert.False(trashList.IsTabStop);
            Assert.False(listScrollViewer.Focusable);
            Assert.False(listScrollViewer.IsTabStop);

            var firstContainer = Assert.IsType<ListBoxItem>(
                trashList.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.True(firstContainer.Focusable);
            Assert.True(firstContainer.IsTabStop);
            Assert.Equal(
                KeyboardNavigationMode.Continue,
                KeyboardNavigation.GetTabNavigation(firstContainer));

            var rowButtons = FindVisualChildren<Button>(firstContainer).ToArray();
            Assert.Equal(2, rowButtons.Length);
            Assert.All(rowButtons, button =>
            {
                Assert.True(button.Focusable);
                Assert.True(button.IsTabStop);
            });

            var itemBorder = Assert.IsType<Border>(
                firstContainer.Template.FindName("ItemBorder", firstContainer));

            Assert.Same(rowButtons[0], Keyboard.Focus(rowButtons[0]));
            Assert.True(firstContainer.IsKeyboardFocusWithin);
            Assert.Equal(window.FindResource("AccentBrush"), itemBorder.BorderBrush);

            var titleText = FindVisualChildren<TextBlock>(firstContainer).First();
            RaisePreviewMouseLeftButtonDown(titleText);
            DrainPendingOperations(window.Dispatcher);

            Assert.Same(firstContainer, Keyboard.FocusedElement);
            Assert.True(firstContainer.IsKeyboardFocusWithin);
            Assert.Equal(window.FindResource("AccentBrush"), itemBorder.BorderBrush);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 20000)]
    public void TabNavigation_TraversesEveryTrashRowAndActionAcrossVirtualizedViewport()
    {
        WpfTestHost.Run(_ =>
        {
            var vault = new Vault();
            foreach (var index in Enumerable.Range(0, 30))
            {
                var entry = new VaultEntry
                {
                    Title = $"Trash entry {index:D2}",
                    Username = $"user{index:D2}"
                };

                vault.AddEntry(entry);
                vault.SoftDelete(entry.Id);
            }

            var viewModel = new TrashViewModel(
                vault,
                () => Task.FromResult(true),
                () => Task.FromResult(true),
                () => true,
                _ => false);
            var window = new TrashWindow(viewModel);

            var searchBox = Assert.IsType<SearchBox>(window.FindName("TrashSearchBox"));
            var trashList = Assert.IsType<ListBox>(window.FindName("TrashList"));
            var closeButton = Assert.IsType<Button>(window.FindName("CloseButton"));

            window.Show();
            window.UpdateLayout();

            var searchInput = Assert.Single(FindVisualChildren<TextBox>(searchBox));
            var scrollViewer = Assert.Single(FindVisualChildren<ScrollViewer>(trashList));

            scrollViewer.ScrollToBottom();
            window.UpdateLayout();

            Assert.True(VirtualizingPanel.GetIsVirtualizing(trashList));
            Assert.Null(trashList.ItemContainerGenerator.ContainerFromIndex(0));

            Assert.Same(searchInput, Keyboard.Focus(searchInput));
            var searchTab = RaisePreviewKeyDown(searchInput, Key.Tab);
            Assert.True(searchTab.Handled);
            DrainPendingOperations(window.Dispatcher);

            var totalFocusStops = viewModel.Items.Count * 3;
            for (var focusIndex = 0; focusIndex < totalFocusStops; focusIndex++)
            {
                var rowIndex = focusIndex / 3;
                var stopIndex = focusIndex % 3;
                var container = Assert.IsType<ListBoxItem>(
                    trashList.ItemContainerGenerator.ContainerFromIndex(rowIndex));
                var rowButtons = FindVisualChildren<Button>(container).ToArray();

                Assert.Equal(2, rowButtons.Length);
                Assert.Same(
                    stopIndex switch
                    {
                        0 => container,
                        1 => rowButtons[0],
                        _ => rowButtons[1]
                    },
                    Keyboard.FocusedElement);

                var focusedElement = Assert.IsAssignableFrom<UIElement>(
                    Keyboard.FocusedElement);
                var tab = RaisePreviewKeyDown(focusedElement, Key.Tab);

                Assert.True(tab.Handled);
                DrainPendingOperations(window.Dispatcher);

                if (focusIndex < totalFocusStops - 1)
                {
                    var nextFocusIndex = focusIndex + 1;
                    var nextRowIndex = nextFocusIndex / 3;
                    var nextStopIndex = nextFocusIndex % 3;
                    var nextContainer = Assert.IsType<ListBoxItem>(
                        trashList.ItemContainerGenerator.ContainerFromIndex(nextRowIndex));
                    var nextButtons = FindVisualChildren<Button>(nextContainer).ToArray();

                    Assert.Same(
                        nextStopIndex switch
                        {
                            0 => nextContainer,
                            1 => nextButtons[0],
                            _ => nextButtons[1]
                        },
                        Keyboard.FocusedElement);
                }
            }

            Assert.Same(closeButton, Keyboard.FocusedElement);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    private static KeyEventArgs RaisePreviewKeyDown(UIElement target, Key key)
    {
        var presentationSource = PresentationSource.FromVisual(target);
        Assert.NotNull(presentationSource);

        var args = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            presentationSource!,
            Environment.TickCount,
            key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };

        target.RaiseEvent(args);
        return args;
    }

    private static MouseButtonEventArgs RaisePreviewMouseLeftButtonDown(UIElement target)
    {
        var args = new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent
        };

        target.RaiseEvent(args);
        return args;
    }

    private static void DrainPendingOperations(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

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
