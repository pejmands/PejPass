using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Wpf.Records;
using PejPass.Wpf.Services;
using PejPass.Wpf.Views;
using System.Windows;

namespace PejPass.Wpf.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly UpdateService _updateService;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isChecking;

    [ObservableProperty]
    private bool hasUpdate;

    [ObservableProperty]
    private string? downloadUrl;

    [ObservableProperty]
    private string? latestVersion;

    public string AppName => AppInfoService.Name;

    public string VersionDisplay => $"v{AppInfoService.Version}";

    public AboutViewModel(UpdateService updateService)
    {
        _updateService = updateService;
        StatusMessage = _updateService.DefaultStatusMessage;
    }

    /// <summary>
    /// Parameterless constructor kept for designer / simple new() sites.
    /// Prefer the DI constructor in production code.
    /// </summary>
    public AboutViewModel() : this(new UpdateService())
    {
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        IsChecking = true;
        HasUpdate = false;
        DownloadUrl = null;
        LatestVersion = null;
        StatusMessage = "Checking for updates…";

        try
        {
            var result = await _updateService
                .CheckForUpdatesAsync(_cts.Token)
                .ConfigureAwait(true);

            ApplyResult(result);
        }
        finally
        {
            IsChecking = false;
            CheckForUpdatesCommand.NotifyCanExecuteChanged();
            OpenDownloadCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCheckForUpdates() => !IsChecking;

    private void ApplyResult(UpdateCheckResult result)
    {
        StatusMessage = result.Message;
        HasUpdate = result.Status == UpdateCheckStatus.UpdateAvailable;
        LatestVersion = result.LatestVersion;
        DownloadUrl = result.DownloadUrl;
    }

    [RelayCommand(CanExecute = nameof(CanOpenDownload))]
    private void OpenDownload()
    {
        if (!string.IsNullOrWhiteSpace(DownloadUrl))
            _updateService.OpenUrl(DownloadUrl);
        else
            _updateService.OpenReleasesPage();
    }

    private bool CanOpenDownload() =>
        HasUpdate && !IsChecking;

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
    private void OpenGitHub() =>
        _updateService.OpenRepositoryPage();

    public void CancelPendingCheck()
    {
        _cts?.Cancel();
    }
}
