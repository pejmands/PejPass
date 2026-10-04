using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly UpdateService _updateService;

    [ObservableProperty]
    private string updateStatus;

    public string Version =>
        $"v{AppInfoService.Version}";

    public string RepositoryUrl =>
        AppInfoService.RepositoryUrl;

    public AboutViewModel()
    {
        _updateService = new UpdateService();

        UpdateStatus = _updateService.StatusMessage;
    }

    [RelayCommand]
    private void CheckForUpdates()
    {
        UpdateStatus = _updateService.StatusMessage;
    }
}
