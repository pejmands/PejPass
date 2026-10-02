using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.Windows;

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

    private void OnFaviconsReady()
    {
        try { TrashList.Items.Refresh(); }
        catch { /* disposing */ }
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
