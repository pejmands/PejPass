using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace PejPass.Wpf.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var viewModel = new AboutViewModel();

        DataContext = viewModel;

        VersionText.Text = viewModel.Version;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void GitHub_Click(object sender, MouseButtonEventArgs e)
    {
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppInfoService.RepositoryUrl,
                UseShellExecute = true
            });
    }
}
