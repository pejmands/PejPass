using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Domain.Policies;
using PejPass.Domain.Security;
using PejPass.Domain.Settings;
using PejPass.Wpf.Services;
using System.IO;
using System.Windows;

namespace PejPass.Wpf.ViewModels;

public sealed record RecentVaultItem(string Path, string DisplayName);

public partial class LoginViewModel : ObservableObject
{
    private readonly VaultService _vaultService;
    private readonly AppSettings _settings;
    private readonly VaultSession _vaultSession;

    public event EventHandler? RequestClose;

    public event EventHandler? RecentVaultSelected;

    public ObservableCollection<RecentVaultItem> RecentVaults { get; } = [];

    [ObservableProperty]
    public partial bool HasRecentVaults { get; set; }

    [ObservableProperty]
    public partial string VaultPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MasterPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmMasterPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? VaultPathError { get; set; }

    [ObservableProperty]
    public partial string? PasswordError { get; set; }

    [ObservableProperty]
    public partial string? ConfirmPasswordError { get; set; }

    public event EventHandler? ValidationFailed;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOpenMode { get; set; } = true;

    [ObservableProperty]
    public partial bool IsCreateMode { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool ShowWindowsHello { get; set; }

    [ObservableProperty]
    public partial string MasterPasswordStrengthLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double MasterPasswordStrengthProgress { get; set; }

    [ObservableProperty]
    public partial int MasterPasswordStrengthLevel { get; set; }

    [ObservableProperty]
    public partial bool ShowMasterPasswordStrength { get; set; }

    public LoginViewModel(
        VaultService vaultService,
        AppSettings settings,
        VaultSession vaultSession)
    {
        _vaultService = vaultService;
        _settings = settings;
        _vaultSession = vaultSession;

        var recentPaths = NormalizeRecentPaths(_settings.RecentVaultPaths);

        foreach (var path in recentPaths.Take(5))
        {
            RecentVaults.Add(new RecentVaultItem(
                path,
                GetVaultDisplayName(path)));
        }

        HasRecentVaults = RecentVaults.Count > 0;

        if (RecentVaults.Count > 0)
        {
            VaultPath = RecentVaults[0].Path;
        }
        else
        {
            VaultPath = GetDefaultVaultPath();

            if (File.Exists(VaultPath))
            {
                IsOpenMode = true;
                IsCreateMode = false;
            }
            else
            {
                IsCreateMode = true;
                IsOpenMode = false;
            }
        }
    }

    private static List<string> NormalizeRecentPaths(IEnumerable<string>? paths)
    {
        var normalized = new List<string>();

        foreach (var path in paths ?? [])
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var candidate = NormalizePathOrOriginal(path);

            if (!normalized.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                normalized.Add(candidate);

            if (normalized.Count == 5)
                break;
        }

        return normalized;
    }

    private static string NormalizePathOrOriginal(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static string GetVaultDisplayName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static string GetDefaultVaultPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PejPass");

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "vault.pejpass");
    }

    [RelayCommand]
    private void SelectRecentVault(RecentVaultItem? item)
    {
        if (item is null)
            return;

        IsOpenMode = true;
        IsCreateMode = false;
        VaultPath = item.Path;
        ResetModeState();
        RecentVaultSelected?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemoveRecentVault(RecentVaultItem? item)
    {
        if (item is null)
            return;

        var existing = RecentVaults.FirstOrDefault(recent =>
            string.Equals(recent.Path, item.Path, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
            return;

        RecentVaults.Remove(existing);
        SaveRecentVaults();
    }

    [RelayCommand]
    private void ClearRecentVaults()
    {
        RecentVaults.Clear();
        SaveRecentVaults();
    }

    private void SaveRecentVaults()
    {
        HasRecentVaults = RecentVaults.Count > 0;
        SaveRecentVaults();
    }

    private void RecordRecentVault(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        var paths = new List<string> { normalizedPath };

        paths.AddRange(
            RecentVaults
                .Select(item => item.Path)
                .Where(existing => !string.Equals(
                    existing,
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase)));

        RecentVaults.Clear();

        foreach (var recentPath in paths.Take(5))
        {
            RecentVaults.Add(new RecentVaultItem(
                recentPath,
                GetVaultDisplayName(recentPath)));
        }

        HasRecentVaults = RecentVaults.Count > 0;
        _settings.RecentVaultPaths = RecentVaults
            .Select(item => item.Path)
            .ToList();

        SettingsStore.Save(_settings);
    }

    /// <summary>
    /// Called when the user double-clicks a .pejpass file (or passes a path on the command line).
    /// Switches to Open mode and points at that file.
    /// </summary>
    public void ApplyExternalVaultPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            path = Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch
        {
            return;
        }

        IsOpenMode = true;
        IsCreateMode = false;
        VaultPath = path;
        VaultPathError = null;
        PasswordError = null;
        StatusMessage = File.Exists(path)
            ? "Vault selected from file. Enter master password to unlock."
            : "Vault file not found at the given path.";

        _ = RefreshWindowsHelloVisibilityAsync();
    }

    partial void OnIsCreateModeChanged(bool value)
    {
        if (value)
        {
            IsOpenMode = false;
            ResetModeState();
        }

        OnPropertyChanged(nameof(SubmitButtonText));

        _ = RefreshWindowsHelloVisibilityAsync();
        UpdateMasterPasswordStrength();
    }

    partial void OnIsOpenModeChanged(bool value)
    {
        if (value)
        {
            IsCreateMode = false;
            ResetModeState();
        }

        OnPropertyChanged(nameof(SubmitButtonText));

        _ = RefreshWindowsHelloVisibilityAsync();
    }

    public string SubmitButtonText =>
        IsCreateMode ? "Create Vault" : "Unlock";

    partial void OnVaultPathChanged(string value) =>
        _ = RefreshWindowsHelloVisibilityAsync();

    partial void OnMasterPasswordChanged(string value) =>
        UpdateMasterPasswordStrength();

    partial void OnConfirmMasterPasswordChanged(string value)
    {
        if (!string.IsNullOrEmpty(ConfirmPasswordError))
        {
            ConfirmPasswordError = null;
        }
    }

    private void UpdateMasterPasswordStrength()
    {
        if (!IsCreateMode)
        {
            ShowMasterPasswordStrength = false;
            MasterPasswordStrengthLabel = string.Empty;
            MasterPasswordStrengthProgress = 0;
            MasterPasswordStrengthLevel = 0;
            return;
        }

        var level = PasswordStrength.Evaluate(MasterPassword);

        MasterPasswordStrengthLevel = (int)level;
        MasterPasswordStrengthLabel = PasswordStrength.GetLabel(level);
        MasterPasswordStrengthProgress = PasswordStrength.GetProgress(level);
        ShowMasterPasswordStrength =
            level != PasswordStrengthLevel.Empty;
    }

    public async Task RefreshWindowsHelloVisibilityAsync()
    {
        try
        {
            ShowWindowsHello =
                _settings.WindowsHelloEnabled
                && IsOpenMode
                && !string.IsNullOrWhiteSpace(VaultPath)
                && SessionPasswordCache.HasCacheFor(VaultPath)
                && await WindowsHelloHelper.IsAvailableAsync();
        }
        catch
        {
            ShowWindowsHello = false;
        }
    }

    private void ResetModeState()
    {
        MasterPassword = string.Empty;
        ConfirmMasterPassword = string.Empty;

        VaultPathError = null;
        PasswordError = null;
        ConfirmPasswordError = null;
        StatusMessage = string.Empty;
    }

    private string GetInitialVaultDirectory()
    {
        if (RecentVaults.Count > 0)
        {
            try
            {
                var recentDirectory = Path.GetDirectoryName(RecentVaults[0].Path);

                if (!string.IsNullOrWhiteSpace(recentDirectory) &&
                    Directory.Exists(recentDirectory))
                {
                    return recentDirectory;
                }
            }
            catch
            {
                // Fall back to the application data directory.
            }
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PejPass");

        Directory.CreateDirectory(directory);
        return directory;
    }

    [RelayCommand]
    private void Browse()
    {
        if (IsCreateMode)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PejPass Vault (*.pejpass)|*.pejpass",
                DefaultExt = ".pejpass",
                FileName = "vault.pejpass",
                InitialDirectory = GetInitialVaultDirectory(),
                OverwritePrompt = true
            };

            if (dlg.ShowDialog() == true)
                VaultPath = EnsureVaultExtension(dlg.FileName);
        }
        else
        {
            var dlg = new OpenFileDialog
            {
                Filter = "PejPass Vault (*.pejpass)|*.pejpass",
                InitialDirectory = GetInitialVaultDirectory()
            };

            if (dlg.ShowDialog() == true)
                VaultPath = dlg.FileName;
        }
    }

    private static string EnsureVaultExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            string.Equals(
                Path.GetExtension(path),
                ".pejpass",
                StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return path + ".pejpass";
    }

    [RelayCommand]
    private async Task UnlockWithHelloAsync()
    {
        VaultPathError = null;
        PasswordError = null;
        StatusMessage = string.Empty;

        if (!IsOpenMode)
        {
            PasswordError =
                "Windows Hello is only for opening an existing vault.";
            return;
        }

        if (string.IsNullOrWhiteSpace(VaultPath) ||
            !File.Exists(VaultPath))
        {
            VaultPathError =
                "Select an existing vault file first.";
            return;
        }

        if (!SessionPasswordCache.HasCacheFor(VaultPath))
        {
            PasswordError =
                "Unlock once with your master password first, then Windows Hello will be available until you exit the app.";
            return;
        }

        var owner =
            System.Windows.Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive)
            ?? System.Windows.Application.Current?.MainWindow;

        if (owner is null)
            return;

        IsBusy = true;

        try
        {
            StatusMessage = "Waiting for Windows Hello…";

            var verified =
                await WindowsHelloHelper.VerifyAsync(
                    owner,
                    "Unlock PejPass");

            if (!verified)
            {
                PasswordError =
                    "Windows Hello verification failed or was cancelled.";
                StatusMessage = string.Empty;
                return;
            }

            if (!SessionPasswordCache.TryRestore(
                    VaultPath,
                    out var password))
            {
                PasswordError =
                    "Could not restore the session. Enter your master password.";
                StatusMessage = string.Empty;
                return;
            }

            StatusMessage = "Unlocking vault…";

            var vault =
                await _vaultService.OpenVaultAsync(
                    VaultPath,
                    password);

            _vaultSession.Open(
                vault,
                VaultPath,
                password);

            SessionPasswordCache.Store(
                VaultPath,
                password);

            RecordRecentVault(VaultPath);

            StatusMessage = "Vault unlocked.";
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            PasswordError = ex.Message;
            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        VaultPathError = null;
        PasswordError = null;
        ConfirmPasswordError = null;
        StatusMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(VaultPath))
        {
            VaultPathError = "Please select a vault path.";
            ValidationFailed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (IsCreateMode)
        {
            VaultPath = EnsureVaultExtension(VaultPath);

            var validation =
                MasterPasswordPolicy.Validate(MasterPassword);

            if (!validation.IsValid)
            {
                PasswordError = validation.ErrorMessage;
                ValidationFailed?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (MasterPassword != ConfirmMasterPassword)
            {
                ConfirmPasswordError =
                    "Passwords do not match.";

                ValidationFailed?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (File.Exists(VaultPath))
            {
                VaultPathError =
                    "A vault already exists here. Switch to «Open vault», or pick another path.";
                ValidationFailed?.Invoke(this, EventArgs.Empty);
                return;
            }
        }
        else
        {
            if (!File.Exists(VaultPath))
            {
                VaultPathError =
                    "No vault at this path. Use «Create vault» or Browse to an existing file.";
                ValidationFailed?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        IsBusy = true;

        try
        {
            Vault vault;

            if (IsCreateMode)
            {
                StatusMessage =
                    "Creating vault (Argon2id key derivation may take a moment)...";

                vault =
                    await _vaultService.CreateVaultAsync(
                        VaultPath,
                        MasterPassword);

                StatusMessage = "Vault created successfully.";
            }
            else
            {
                StatusMessage = "Unlocking vault...";

                vault =
                    await _vaultService.OpenVaultAsync(
                        VaultPath,
                        MasterPassword);

                StatusMessage = "Vault unlocked.";
            }

            _vaultSession.Open(
                vault,
                VaultPath,
                MasterPassword);

            if (_settings.WindowsHelloEnabled)
            {
                SessionPasswordCache.Store(
                    VaultPath,
                    MasterPassword);
            }
            else
            {
                SessionPasswordCache.Clear();
            }

            RecordRecentVault(VaultPath);

            MasterPassword = string.Empty;
            ConfirmMasterPassword = string.Empty;

            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            PasswordError = ex.Message;
            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
