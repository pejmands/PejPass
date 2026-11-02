using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PejPass.Domain.Entities;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Controls;
using System.Windows.Media;

namespace PejPass.Wpf.Tests.Windowing;

public sealed class KeyboardFocusVisibilityTests
{
    [Fact(Timeout = 15000)]
    public void ScrollViewerTemplate_DoesNotExposeScrollBarsAsTabStops()
    {
        WpfTestHost.Run(_ =>
        {
            var content = new StackPanel
            {
                Width = 600,
                Height = 600
            };

            var viewer = new ScrollViewer
            {
                Width = 200,
                Height = 120,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            };

            var window = new Window
            {
                Width = 260,
                Height = 180,
                Content = viewer,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -1000,
                Top = -1000
            };

            window.Show();
            window.UpdateLayout();
            viewer.ApplyTemplate();

            var vertical = Assert.IsType<ScrollBar>(
                viewer.Template.FindName("PART_VerticalScrollBar", viewer));
            var horizontal = Assert.IsType<ScrollBar>(
                viewer.Template.FindName("PART_HorizontalScrollBar", viewer));

            Assert.False(vertical.IsTabStop);
            Assert.False(vertical.Focusable);
            Assert.False(horizontal.IsTabStop);
            Assert.False(horizontal.Focusable);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void MainWindowGridSplitter_ShowsFocusRingWhenFocusedByKeyboard()
    {
        WpfTestHost.Run(host =>
        {
            var window = host.CreateMainWindow();

            window.Show();
            window.UpdateLayout();

            var splitter = FindVisualChild<GridSplitter>(window);
            Assert.NotNull(splitter);

            splitter!.ApplyTemplate();
            var focusRing = Assert.IsType<Border>(
                splitter.Template.FindName("SplitterFocusRing", splitter));

            Assert.Equal(Visibility.Collapsed, focusRing.Visibility);

            Keyboard.Focus(splitter);
            window.UpdateLayout();

            Assert.True(splitter.IsKeyboardFocused);
            Assert.Equal(Visibility.Visible, focusRing.Visibility);

            window.Hide();
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void MainWindow_TotpCopy_TabKeepsCustomFieldsAndTagChipsVisible()
    {
        WpfTestHost.Run(host =>
        {
            const int customFieldCount = 18;
            const int tagCount = 24;

            var window = host.CreateMainWindow();
            window.Height = 560;
            System.Windows.Application.Current!.MainWindow = window;

            var viewModel = Assert.IsType<MainViewModel>(window.DataContext);
            var tagNames = Enumerable.Range(1, tagCount)
                .Select(index => $"focus-tag-{index:D2}")
                .ToArray();

            var entry = new VaultEntry
            {
                Title = "TOTP entry",
                TotpSecret = "JBSWY3DPEHPK3PXP",
                Notes = string.Join(" ", Enumerable.Repeat(
                    "A long note used to exercise the nested notes scroll viewer.",
                    120)),
                Tags = tagNames.ToList(),
                CustomFields = Enumerable.Range(1, customFieldCount)
                    .Select(index => new CustomField
                    {
                        Name = $"Secret field {index:D2}",
                        Value = $"secret-value-{index:D2}",
                        IsSecret = true
                    })
                    .ToList()
            };

            viewModel.Entries.Add(entry);
            viewModel.FilteredEntries.Add(entry);
            viewModel.TagFilters.Add(new TagFilterItem
            {
                Name = "All",
                Count = 1,
                IsAll = true,
                IsSelected = true
            });

            foreach (var tagName in tagNames)
            {
                viewModel.TagFilters.Add(new TagFilterItem
                {
                    Name = tagName,
                    Count = 1
                });
            }

            viewModel.ShowTagFilters = true;
            viewModel.SelectedEntry = entry;

            window.Show();
            window.UpdateLayout();

            var copyButton = Assert.IsType<Button>(
                window.FindName("CopyTotpButton"));
            var editButton = Assert.IsType<Button>(
                window.FindName("EditEntryButton"));
            var detailsScrollViewer = Assert.IsType<ScrollViewer>(
                window.FindName("EntryDetailsScrollViewer"));
            var notesScrollViewer = Assert.IsType<ScrollViewer>(
                window.FindName("NotesScrollViewer"));
            var tagStrip = Assert.IsType<TagChipStrip>(
                FindVisualChild<TagChipStrip>(window));
            var tagStripScrollViewer = Assert.IsType<ScrollViewer>(
                FindVisualChild<ScrollViewer>(tagStrip));

            Assert.Equal(Visibility.Visible, copyButton.Visibility);
            Assert.Equal(Visibility.Visible, notesScrollViewer.Visibility);
            Assert.True(notesScrollViewer.ScrollableHeight > 0);
            Assert.True(tagStripScrollViewer.ScrollableWidth > 0);
            Assert.Equal(tagCount + 1, FindVisualChildren<Button>(tagStrip).Count());
            Assert.Equal(customFieldCount, viewModel.DisplayCustomFields.Count);

            Assert.False(detailsScrollViewer.Focusable);
            Assert.False(detailsScrollViewer.IsTabStop);
            Assert.False(notesScrollViewer.Focusable);
            Assert.False(notesScrollViewer.IsTabStop);
            Assert.True(Keyboard.Focus(copyButton) is not null);
            Assert.Same(copyButton, Keyboard.FocusedElement);

            var customFieldFocusCount = 0;
            var tagChipFocusCount = 0;
            var returnedToStart = false;

            for (var index = 0; index < 160; index++)
            {
                PressTab(window);
                window.UpdateLayout();

                var focused = Assert.IsAssignableFrom<FrameworkElement>(
                    Keyboard.FocusedElement);

                Assert.True(
                    focused.IsVisible,
                    $"Tab moved focus to an invisible element: {DescribeFocus(focused)}.");

                if (ReferenceEquals(focused, copyButton))
                {
                    returnedToStart = true;
                    break;
                }

                if (FindVisualAncestor<ItemsControl>(focused) is { } itemsControl &&
                    ReferenceEquals(itemsControl.ItemsSource, viewModel.DisplayCustomFields))
                {
                    customFieldFocusCount++;
                }

                if (FindVisualAncestor<TagChipStrip>(focused) is not null)
                    tagChipFocusCount++;

                AssertFocusedElementIsVisibleInAncestorScrollViewers(focused, index + 1);
            }

            Assert.True(returnedToStart, "Tab navigation did not complete a full cycle back to the TOTP Copy button.");
            Assert.True(customFieldFocusCount > 0, "The Tab cycle never reached a Custom Fields action button.");
            Assert.True(tagChipFocusCount > 0, "The Tab cycle never reached a tag filter chip.");

            Assert.True(editButton.IsVisible);

            window.Hide();
            viewModel.StopBackgroundTimers();
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void SliderTemplate_ShowsFocusRingWhenFocusedByKeyboard()
    {
        WpfTestHost.Run(_ =>
        {
            var slider = new Slider
            {
                Width = 200,
                Minimum = 0,
                Maximum = 100,
                Value = 50
            };

            var window = new Window
            {
                Width = 260,
                Height = 100,
                Content = slider,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -1000,
                Top = -1000
            };

            window.Show();
            window.UpdateLayout();
            slider.ApplyTemplate();

            var focusRing = Assert.IsType<Border>(
                slider.Template.FindName("FocusRing", slider));
            var track = Assert.IsType<Track>(
                slider.Template.FindName("PART_Track", slider));

            Assert.False(track.DecreaseRepeatButton.IsTabStop);
            Assert.False(track.DecreaseRepeatButton.Focusable);
            Assert.False(track.IncreaseRepeatButton.IsTabStop);
            Assert.False(track.IncreaseRepeatButton.Focusable);
            Assert.Equal(Visibility.Collapsed, focusRing.Visibility);

            Keyboard.Focus(slider);
            window.UpdateLayout();

            Assert.True(slider.IsKeyboardFocused);
            Assert.Equal(Visibility.Visible, focusRing.Visibility);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    private static void AssertFocusedElementIsVisibleInAncestorScrollViewers(
        FrameworkElement focusedElement,
        int tabIndex)
    {
        var current = VisualTreeHelper.GetParent(focusedElement);

        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer &&
                scrollViewer.ViewportWidth > 0 &&
                scrollViewer.ViewportHeight > 0)
            {
                var bounds = focusedElement
                    .TransformToAncestor(scrollViewer)
                    .TransformBounds(new Rect(focusedElement.RenderSize));
                var viewport = new Rect(
                    0,
                    0,
                    scrollViewer.ViewportWidth,
                    scrollViewer.ViewportHeight);

                Assert.True(
                    IsVisibleWithin(bounds.Left, bounds.Right, viewport.Left, viewport.Right),
                    $"Tab step {tabIndex}: {DescribeFocus(focusedElement)} is horizontally outside {scrollViewer.Name}: bounds={bounds}, viewport={viewport}.");
                Assert.True(
                    IsVisibleWithin(bounds.Top, bounds.Bottom, viewport.Top, viewport.Bottom),
                    $"Tab step {tabIndex}: {DescribeFocus(focusedElement)} is vertically outside {scrollViewer.Name}: bounds={bounds}, viewport={viewport}.");
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }

    private static bool IsVisibleWithin(
        double start,
        double end,
        double viewportStart,
        double viewportEnd)
    {
        const double tolerance = 1.5;
        return start >= viewportStart - tolerance &&
               end <= viewportEnd + tolerance;
    }

    private static T? FindVisualAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(element);

        while (current is not null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
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

    private static void PressTab(Window window)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("The test window has no presentation source.");

        var keyEvent = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            Key.Tab)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };

        InputManager.Current.ProcessInput(keyEvent);
    }

    private static string DescribeFocus(DependencyObject? element)
    {
        if (element is null)
            return "<null>";

        var frameworkElement = element as FrameworkElement;
        var details = frameworkElement is null
            ? string.Empty
            : $", Name='{frameworkElement.Name}', Visibility={frameworkElement.Visibility}, IsVisible={frameworkElement.IsVisible}, Focusable={frameworkElement.Focusable}, IsTabStop={(frameworkElement is Control control ? control.IsTabStop.ToString() : "n/a")}, Size={frameworkElement.RenderSize}";

        return $"{element.GetType().FullName}{details}";
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
}
