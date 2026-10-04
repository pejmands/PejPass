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
    private bool isDownloading;

    [ObservableProperty]
    private bool hasUpdate;

    [ObservableProperty]
    private string? downloadUrl;

    [ObservableProperty]
    private string? latestVersion;

    [ObservableProperty]
    private double downloadProgress;

    [ObservableProperty]
    private string? downloadedPath;

    public string AppName => AppInfoService.Name;

    public string VersionDisplay => $"v{AppInfoService.Version}";

    public bool IsBusy => IsChecking || IsDownloading;

    public AboutViewModel(UpdateService updateService)
    {
        _updateService = updateService;
        StatusMessage = _updateService.DefaultStatusMessage;
    }

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
        DownloadedPath = null;
        DownloadProgress = 0;
        StatusMessage = "Checking for updates…";
        NotifyBusy();

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
            NotifyBusy();
        }
    }

    private bool CanCheckForUpdates() => !IsBusy;

    private void ApplyResult(UpdateCheckResult result)
    {
        StatusMessage = result.Message;
        HasUpdate = result.Status == UpdateCheckStatus.UpdateAvailable;
        LatestVersion = result.LatestVersion;
        DownloadUrl = result.DownloadUrl;
    }

    [RelayCommand(CanExecute = nameof(CanDownloadUpdate))]
    private async Task DownloadUpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(DownloadUrl))
        {
            _updateService.OpenReleasesPage();
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        IsDownloading = true;
        DownloadProgress = 0;
        DownloadedPath = null;
        StatusMessage = $"Downloading v{LatestVersion}…";
        NotifyBusy();

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                StatusMessage = $"Downloading v{LatestVersion}… {p:P0}";
            });

            var path = await _updateService
                .DownloadUpdateAsync(DownloadUrl, progress, _cts.Token)
                .ConfigureAwait(true);

            DownloadedPath = path;
            StatusMessage = $"Downloaded to Downloads folder.";
            _updateService.OpenFolder(path);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            NotifyBusy();
        }
    }

    private bool CanDownloadUpdate() =>
        HasUpdate && !IsBusy && !string.IsNullOrWhiteSpace(DownloadUrl);

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

    private void NotifyBusy()
    {
        OnPropertyChanged(nameof(IsBusy));
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadUpdateCommand.NotifyCanExecuteChanged();
    }

    public void CancelPending()
    {
        _cts?.Cancel();
    }
}
