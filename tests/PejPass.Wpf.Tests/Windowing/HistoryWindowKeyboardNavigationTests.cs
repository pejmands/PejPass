using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PejPass.Wpf.Tests.Windowing;

public sealed class HistoryWindowKeyboardNavigationTests
{
    [Fact(Timeout = 15000)]
    public void TabFromSearch_SelectsHistoryRow_AndMovesToChangedFieldSelection()
    {
        WpfTestHost.Run(_ =>
        {
            using var session = new VaultSession();
            using var material = CreateMaterial();

            var entry = new VaultEntry
            {
                Title = "Current title"
            };
            var history = new EntryHistoryItem
            {
                EntryId = entry.Id,
                Title = "Previous title",
                ChangedAt = DateTimeOffset.UtcNow
            };
            var vault = new Vault
            {
                Entries = [entry],
                History = [history]
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

            var titleBar = Assert.IsType<Controls.AppTitleBar>(
                window.FindName("HistoryTitleBar"));
            var searchBox = Assert.IsType<Controls.SearchBox>(
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
            var historyItem = Assert.IsType<ListBoxItem>(
                historyList.ItemContainerGenerator.ContainerFromIndex(0));
            var expander = Assert.Single(FindVisualChildren<Expander>(historyItem));

            Assert.False(titleBar.Focusable);
            Assert.False(titleBar.IsTabStop);
            Assert.False(searchBox.Focusable);
            Assert.False(searchBox.IsTabStop);
            Assert.False(historyList.Focusable);
            Assert.False(historyList.IsTabStop);
            Assert.False(historyScrollViewer.Focusable);
            Assert.False(historyScrollViewer.IsTabStop);
            Assert.False(historyItem.Focusable);
            Assert.False(historyItem.IsTabStop);
            Assert.True(expander.Focusable);
            Assert.True(expander.IsTabStop);
            Assert.False(comparisonScrollViewer.Focusable);
            Assert.False(comparisonScrollViewer.IsTabStop);
            Assert.False(comparisonItems.Focusable);
            Assert.False(comparisonItems.IsTabStop);
            Assert.False(busyProgressBar.Focusable);
            Assert.False(busyProgressBar.IsTabStop);

            Assert.Same(searchInput, Keyboard.Focus(searchInput));
            Assert.True(searchInput.MoveFocus(
                new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.True(expander.IsKeyboardFocusWithin);
            Assert.Same(viewModel.Items[0], viewModel.SelectedItem);

            var firstChangedFieldCheckBox = Assert.Single(
                FindVisualChildren<CheckBox>(comparisonItems)
                    .Where(checkBox => checkBox.IsVisible));
            Assert.True(firstChangedFieldCheckBox.IsTabStop);

            var focusedElement = Assert.IsAssignableFrom<UIElement>(
                Keyboard.FocusedElement);
            Assert.True(focusedElement.MoveFocus(
                new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(firstChangedFieldCheckBox, Keyboard.FocusedElement);

            window.Close();
        }, TestContext.Current.CancellationToken);
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