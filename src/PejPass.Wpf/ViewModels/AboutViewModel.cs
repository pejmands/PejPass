using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Records;
using PejPass.Wpf.Services;
using PejPass.Wpf.Views;

namespace PejPass.Wpf.ViewModels;

public partial class AboutViewModel(UpdateService updateService) : ObservableObject
{
    private readonly UpdateService _updateService = updateService;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = UpdateService.DefaultStatusMessage;

    [ObservableProperty]
    public partial bool IsChecking { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial bool HasUpdate { get; set; }

    [ObservableProperty]
    public partial string? DownloadUrl { get; set; }

    [ObservableProperty]
    public partial string? DownloadSha256 { get; set; }

    [ObservableProperty]
    public partial string? LatestVersion { get; set; }

    [ObservableProperty]
    public partial string? LatestReleased { get; set; }

    [ObservableProperty]
    public partial double DownloadProgress { get; set; }

    [ObservableProperty]
    public partial string? DownloadedPath { get; set; }

    public static string AppName => AppInfoService.Name;

    /// <summary>
    /// Installed build line:
    /// "Version 1.0.4 · Updated September 17, 2026"
    /// </summary>
    public static string VersionDisplay =>
        $"Version {AppInfoService.Version} · {AppInfoService.ReleaseDate}";

    public bool IsBusy => IsChecking || IsDownloading;

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
        DownloadSha256 = null;
        LatestVersion = null;
        LatestReleased = null;
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
        LatestReleased = result.Released;
        DownloadUrl = result.DownloadUrl;
        DownloadSha256 = result.Sha256;
        UpdateAvailability.Set(HasUpdate);
    }

    /// <summary>
    /// If a previous check (e.g. startup auto-check) already found an update,
    /// show Download & Install without requiring another check click.
    /// </summary>
    public void ApplyKnownUpdateState()
    {
        if (IsBusy)
            return;

        var manifest = _updateService.LastManifest;
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
            return;

        if (!UpdateService.IsNewerVersion(manifest.Version, AppInfoService.Version))
            return;

        ApplyResult(UpdateCheckResult.Available(AppInfoService.Version, manifest));
    }

    /// <summary>
    /// Download to %TEMP%, then ask to install and restart.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownloadUpdate))]
    private async Task DownloadUpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(DownloadUrl))
        {
            UpdateService.OpenReleasesPage();
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
                .DownloadUpdateAsync(DownloadUrl, DownloadSha256!, progress, _cts.Token)
                .ConfigureAwait(true);

            DownloadedPath = path;
            StatusMessage = "Download complete.";

            var versionLabel = string.IsNullOrWhiteSpace(LatestVersion)
                ? "the new version"
                : $"v{LatestVersion}";

            var install = DialogService.Confirm(
                $"PejPass {versionLabel} is ready to install.\n\n" +
                "Install now and restart PejPass?\n\n" +
                "Your vault files are not modified by this update.",
                "Install update",
                yesText: "Install & restart",
                noText: "Cancel");

            if (!install)
            {
                TryDeleteTempPackage(path);
                StatusMessage = UpdateService.DefaultStatusMessage;
                return;
            }

            StatusMessage = "Installing update…";
            try
            {
                UpdateService.ApplyPortableUpdateAndRestart(
                    path,
                    LatestVersion ?? "0.0.0",
                    DownloadSha256 ?? string.Empty);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                TryDeleteTempPackage(path);
                StatusMessage = "Install failed: access to the installation folder was denied.";
                DialogService.Error(
                    "PejPass could not write to its installation folder. Move the portable app to a folder you can modify, then try again.",
                    "Install failed");
            }
            catch (InvalidDataException ex)
            {
                TryDeleteTempPackage(path);
                StatusMessage = $"Install failed: {ex.Message}";
                DialogService.Error(
                    $"The update package could not be installed. {ex.Message}\n\nPlease download the update again or install it manually.",
                    "Invalid update package");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
            {
                TryDeleteTempPackage(path);
                StatusMessage = $"Install failed: {ex.Message}";
                DialogService.Error(
                    $"Could not apply the update automatically. {ex.Message}\n\nPlease download and install the update manually.",
                    "Install failed");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download cancelled.";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
            DialogService.Error(
                $"The update server could not provide the package. {ex.Message}",
                "Download failed");
        }
        catch (InvalidDataException ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
            DialogService.Error(ex.Message, "Invalid update package");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Download failed: {ex.Message}";
            DialogService.Error(
                $"The update package could not be saved to the temporary folder. {ex.Message}",
                "Download failed");
        }
        catch (Exception)
        {
            StatusMessage = "Download failed. Check your connection and try again.";
            DialogService.Error(
                "The update could not be downloaded. Check your connection and try again.",
                "Download failed");
        }
        finally
        {
            IsDownloading = false;
            NotifyBusy();
        }
    }

    private bool CanDownloadUpdate() =>
        HasUpdate && !IsBusy && !string.IsNullOrWhiteSpace(DownloadUrl);

    [RelayCommand(CanExecute = nameof(CanOpenDownloadPage))]
    private void OpenDownloadPage()
    {
        if (!string.IsNullOrWhiteSpace(DownloadUrl))
            UpdateService.OpenUrl(DownloadUrl);
        else
            UpdateService.OpenReleasesPage();
    }

    private bool CanOpenDownloadPage() =>
        HasUpdate && !string.IsNullOrWhiteSpace(DownloadUrl);

    private static void TryDeleteTempPackage(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
        catch
        {
        }
    }

    [RelayCommand]
    private static void ShowWhatsNew()
    {
        var window = new WhatsNewWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        window.ShowDialog();
    }

    [RelayCommand]
    private static void OpenGitHub() =>
        UpdateService.OpenRepositoryPage();

    private void NotifyBusy()
    {
        OnPropertyChanged(nameof(IsBusy));
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadUpdateCommand.NotifyCanExecuteChanged();
        OpenDownloadPageCommand.NotifyCanExecuteChanged();
    }

    public void CancelPending()
    {
        _cts?.Cancel();
    }
}
