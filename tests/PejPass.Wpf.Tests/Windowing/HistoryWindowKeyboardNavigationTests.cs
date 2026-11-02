using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Wpf.Controls;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf.Tests.Windowing;

public sealed class HistoryWindowKeyboardNavigationTests
{
    [Fact(Timeout = 20000)]
    public void TabNavigation_TraversesHistoryItemsBeyondTheVirtualizedViewport()
    {
        WpfTestHost.Run(_ =>
        {
            using var session = new VaultSession();
            using var material = CreateMaterial();

            var entry = new VaultEntry
            {
                Title = "Current title"
            };
            var history = Enumerable.Range(0, 30)
                .Select(index => new EntryHistoryItem
                {
                    EntryId = entry.Id,
                    Title = $"Previous title {index:D2}",
                    ChangedAt = DateTimeOffset.UtcNow.AddMinutes(-index)
                })
                .ToList();
            var vault = new Vault
            {
                Entries = [entry],
                History = history
            };

            session.Open(
                vault,
                Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"),
                material);

            using var viewModel = new HistoryViewModel(
                session,
                null!,
                new AppSettings(),
                (_, _) => { });
            var window = new HistoryWindow(viewModel);

            var titleBar = Assert.IsType<AppTitleBar>(
                window.FindName("HistoryTitleBar"));
            var searchBox = Assert.IsType<SearchBox>(
                window.FindName("HistorySearchBox"));
            var historyList = Assert.IsType<ListBox>(
                window.FindName("HistoryItemsListBox"));
            var comparisonScrollViewer = Assert.IsType<ScrollViewer>(
                window.FindName("ComparisonScrollViewer"));
            var comparisonItems = Assert.IsType<ItemsControl>(
                window.FindName("ComparisonFieldsItemsControl"));
            var busyProgressBar = Assert.IsType<ProgressBar>(
                window.FindName("BusyProgressBar"));

            window.Show();
            window.UpdateLayout();

            var searchInput = Assert.Single(FindVisualChildren<TextBox>(searchBox));
            var historyScrollViewer = Assert.Single(
                FindVisualChildren<ScrollViewer>(historyList));

            Assert.Equal(30, viewModel.Items.Count);
            Assert.Null(historyList.ItemContainerGenerator.ContainerFromIndex(20));
            Assert.True(VirtualizingPanel.GetIsVirtualizing(historyList));

            Assert.False(titleBar.Focusable);
            Assert.False(titleBar.IsTabStop);
            Assert.False(searchBox.Focusable);
            Assert.False(searchBox.IsTabStop);
            Assert.False(historyList.Focusable);
            Assert.False(historyList.IsTabStop);
            Assert.False(historyScrollViewer.Focusable);
            Assert.False(historyScrollViewer.IsTabStop);
            Assert.False(comparisonScrollViewer.Focusable);
            Assert.False(comparisonScrollViewer.IsTabStop);
            Assert.False(comparisonItems.Focusable);
            Assert.False(comparisonItems.IsTabStop);
            Assert.False(busyProgressBar.Focusable);
            Assert.False(busyProgressBar.IsTabStop);

            Assert.Same(searchInput, Keyboard.Focus(searchInput));
            var searchTab = RaisePreviewKeyDown(searchInput, Key.Tab);
            Assert.True(searchTab.Handled);
            DrainPendingOperations(window.Dispatcher);

            Assert.Same(viewModel.Items[0], viewModel.SelectedItem);
            var firstContainer = Assert.IsType<ListBoxItem>(
                historyList.ItemContainerGenerator.ContainerFromIndex(0));
            var firstExpander = Assert.Single(FindVisualChildren<Expander>(firstContainer));
            Assert.Same(firstExpander, Keyboard.FocusedElement);

            var cardBorder = Assert.IsType<Border>(VisualTreeHelper.GetParent(firstExpander));
            Assert.True(cardBorder.IsKeyboardFocusWithin);
            Assert.Equal(new Thickness(1), cardBorder.BorderThickness);
            Assert.Equal(window.FindResource("AccentBrush"), cardBorder.BorderBrush);

            for (var index = 1; index <= 18; index++)
            {
                var currentExpander = Assert.IsAssignableFrom<Expander>(
                    Keyboard.FocusedElement);
                var tab = RaisePreviewKeyDown(currentExpander, Key.Tab);

                Assert.True(tab.Handled);
                DrainPendingOperations(window.Dispatcher);

                var container = Assert.IsType<ListBoxItem>(
                    historyList.ItemContainerGenerator.ContainerFromIndex(index));
                var expander = Assert.Single(FindVisualChildren<Expander>(container));

                Assert.Same(expander, Keyboard.FocusedElement);
                Assert.Same(viewModel.Items[index], viewModel.SelectedItem);
            }

            var focusHistoryItem = typeof(HistoryWindow).GetMethod(
                "FocusHistoryItem",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(focusHistoryItem);

            var lastIndex = viewModel.Items.Count - 1;
            focusHistoryItem!.Invoke(window, [lastIndex]);
            DrainPendingOperations(window.Dispatcher);

            var lastContainer = Assert.IsType<ListBoxItem>(
                historyList.ItemContainerGenerator.ContainerFromIndex(lastIndex));
            var lastExpander = Assert.Single(FindVisualChildren<Expander>(lastContainer));
            Assert.Same(lastExpander, Keyboard.FocusedElement);
            Assert.Same(viewModel.Items[lastIndex], viewModel.SelectedItem);

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

    private static void DrainPendingOperations(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static VaultKeyMaterial CreateMaterial()
    {
        var key = new byte[32];
        var salt = new byte[16];
        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(salt);

        try
        {
            return new VaultKeyMaterial(key, salt, Argon2Parameters.Default);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
        }
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
