using PejPass.Wpf.Controls;
using PejPass.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PejPass.Wpf.Tests.Controls;

public sealed class TagChipStripKeyboardNavigationTests
{
    [Fact(Timeout = 15000)]
    public void TabNavigation_FromAddEntry_ReachesEachTagChipOnlyOnce()
    {
        WpfTestHost.Run(_ =>
        {
            var addEntry = new Button { Content = "Add Entry" };
            var afterTags = new Button { Content = "After tags" };
            var strip = new TagChipStrip
            {
                ItemsSource = new TagFilterItem[]
                {
                    new() { Name = "All", Count = 2, IsAll = true },
                    new() { Name = "Work", Count = 1 }
                }
            };

            var panel = new StackPanel();
            panel.Children.Add(addEntry);
            panel.Children.Add(strip);
            panel.Children.Add(afterTags);

            var window = new Window
            {
                Width = 420,
                Height = 180,
                Content = panel,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -1000,
                Top = -1000
            };

            window.Show();
            window.UpdateLayout();

            var scroller = Assert.IsType<ScrollViewer>(FindVisualChild<ScrollViewer>(strip));
            var itemsControl = Assert.IsType<ItemsControl>(FindVisualChild<ItemsControl>(strip));
            var chips = FindVisualChildren<Button>(strip).ToArray();

            Assert.False(strip.Focusable);
            Assert.False(strip.IsTabStop);
            Assert.False(scroller.Focusable);
            Assert.False(scroller.IsTabStop);
            Assert.False(itemsControl.Focusable);
            Assert.False(itemsControl.IsTabStop);
            Assert.Equal(2, chips.Length);
            Assert.Equal("All (2)", chips[0].Content);
            Assert.Equal("Work (1)", chips[1].Content);

            Keyboard.Focus(addEntry);

            Assert.True(addEntry.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(chips[0], Keyboard.FocusedElement);

            Assert.True(chips[0].MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(chips[1], Keyboard.FocusedElement);

            Assert.True(chips[1].MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(afterTags, Keyboard.FocusedElement);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                return match;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
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
