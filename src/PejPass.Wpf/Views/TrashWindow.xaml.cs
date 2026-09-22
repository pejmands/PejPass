using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class TrashWindow : Window
{
    public TrashWindow(TrashViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        FaviconService.FaviconsBatchReady += OnFaviconsReady;
        Loaded += (_, _) =>
            FaviconService.Prefetch(viewModel.Items.Select(i => (i.Url, i.Title)));
        Closed += (_, _) => FaviconService.FaviconsBatchReady -= OnFaviconsReady;
    }

    private void OnFaviconsReady()
    {
        try { TrashList.Items.Refresh(); }
        catch { /* disposing */ }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
