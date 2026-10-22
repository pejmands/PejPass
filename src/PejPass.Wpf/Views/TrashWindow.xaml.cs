using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
