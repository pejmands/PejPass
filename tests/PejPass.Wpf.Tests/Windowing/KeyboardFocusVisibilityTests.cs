using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PejPass.Domain.Entities;
using PejPass.Wpf.ViewModels;
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
    public void MainWindow_TotpCopy_TabKeyScrollsEditIntoView()
    {
        WpfTestHost.Run(host =>
        {
            var window = host.CreateMainWindow();
            window.Height = 560;
            System.Windows.Application.Current!.MainWindow = window;
            var viewModel = Assert.IsType<MainViewModel>(window.DataContext);
            var entry = new VaultEntry
            {
                Title = "TOTP entry",
                TotpSecret = "JBSWY3DPEHPK3PXP",
                Notes = string.Join(" ", Enumerable.Repeat(
                    "A long note used to exercise the nested notes scroll viewer.",
                    120)),
                Tags = Enumerable.Range(1, 60)
                    .Select(index => $"focus-visibility-regression-tag-{index:D2}")
                    .ToList()
            };

            viewModel.Entries.Add(entry);
            viewModel.FilteredEntries.Add(entry);
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

            Assert.Equal(Visibility.Visible, copyButton.Visibility);
            Assert.Equal(Visibility.Visible, notesScrollViewer.Visibility);
            Assert.True(
                notesScrollViewer.ScrollableHeight > 0,
                "The regression entry must produce a scrollable Notes section.");
            Assert.False(detailsScrollViewer.Focusable);
            Assert.False(detailsScrollViewer.IsTabStop);
            Assert.False(notesScrollViewer.Focusable);
            Assert.False(notesScrollViewer.IsTabStop);
            Assert.True(Keyboard.Focus(copyButton) is not null);
            Assert.Same(copyButton, Keyboard.FocusedElement);

            PressTab(window);
            window.UpdateLayout();

            var focused = Keyboard.FocusedElement as DependencyObject;
            Assert.True(
                ReferenceEquals(editButton, focused),
                $"Expected EditEntryButton after a real Tab key input from TOTP Copy, but focus moved to {DescribeFocus(focused)}.");

            Assert.True(editButton.IsKeyboardFocused);
            editButton.ApplyTemplate();
            var editFocusRing = Assert.IsType<Border>(
                editButton.Template.FindName("FocusRing", editButton));
            Assert.Equal(Visibility.Visible, editFocusRing.Visibility);

            var editBounds = editButton
                .TransformToAncestor(detailsScrollViewer)
                .TransformBounds(new Rect(editButton.RenderSize));
            var viewportBounds = new Rect(
                0,
                0,
                detailsScrollViewer.ViewportWidth,
                detailsScrollViewer.ViewportHeight);
            Assert.True(
                viewportBounds.IntersectsWith(editBounds),
                $"EditEntryButton has keyboard focus but remains outside the visible details viewport. Edit bounds: {editBounds}; viewport: {viewportBounds}; vertical offset: {detailsScrollViewer.VerticalOffset}.");

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
            : $", Name='{frameworkElement.Name}', Visibility={frameworkElement.Visibility}, IsVisible={frameworkElement.IsVisible}, Focusable={frameworkElement.Focusable}";

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
