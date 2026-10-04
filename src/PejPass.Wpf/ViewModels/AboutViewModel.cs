using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Wpf.Services;
using PejPass.Wpf.Views;
using System.Windows;

namespace PejPass.Wpf.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly UpdateService _updateService = new();

    [ObservableProperty]
    private string updateStatus;

    public string Version => $"v{AppInfoService.Version}";

    public string RepositoryUrl => AppInfoService.RepositoryUrl;

    public AboutViewModel()
    {
        UpdateStatus = _updateService.StatusMessage;
    }

    [RelayCommand]
    private void CheckForUpdates()
    {
        // Offline-first: we do not phone home.
        // We simply open the Releases page so the user can decide.
        _updateService.OpenReleasesPage();
        UpdateStatus = "Opened GitHub Releases in your browser.";
    }

    [RelayCommand]
    private void ShowWhatsNew()
    {
        var window = new WhatsNewWindow
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        _updateService.OpenRepositoryPage();
    }
}
