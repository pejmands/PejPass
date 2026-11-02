using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
