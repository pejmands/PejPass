using PejPass.Domain.Entities;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf.Tests.Windowing;

public sealed class VaultHealthWindowKeyboardNavigationTests
{
    [Fact(Timeout = 15000)]
    public void TabFromIssueFilter_GoesDirectlyToFirstOpenAction()
    {
        WpfTestHost.Run(_ =>
        {
            var viewModel = new VaultHealthViewModel(
            [
                new VaultEntry
                {
                    Title = "Example account"
                }
            ]);
            var window = new VaultHealthWindow(viewModel);
            var filter = Assert.IsType<ComboBox>(window.FindName("IssueFilterComboBox"));
            var issuesList = Assert.IsType<ListBox>(window.FindName("HealthIssuesList"));
            var progressBar = Assert.IsType<ProgressBar>(window.FindName("ScanProgressBar"));

            window.Show();
            DrainPendingScan(window.Dispatcher);
            window.UpdateLayout();

            Assert.True(viewModel.IsReady);
            Assert.NotEmpty(viewModel.Issues);
            Assert.False(progressBar.Focusable);
            Assert.False(progressBar.IsTabStop);
            Assert.False(issuesList.Focusable);
            Assert.False(issuesList.IsTabStop);

            var firstItem = Assert.IsType<ListBoxItem>(
                issuesList.ItemContainerGenerator.ContainerFromIndex(0));
            var openButton = FindVisualChildren<Button>(firstItem)
                .Single(button => string.Equals(
                    button.Content?.ToString(),
                    "Open",
                    StringComparison.Ordinal));

            Assert.False(firstItem.Focusable);
            Assert.False(firstItem.IsTabStop);

            Assert.Same(filter, Keyboard.Focus(filter));
            Assert.True(filter.MoveFocus(
                new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(openButton, Keyboard.FocusedElement);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    private static void DrainPendingScan(Dispatcher dispatcher)
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
