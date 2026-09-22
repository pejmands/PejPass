using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

public partial class LoginViewModel : ObservableObject
{
    private readonly VaultService _vaultService;
    private readonly AppSettings _settings;

    public event EventHandler? RequestClose;

    public static Vault? CurrentVault { get; private set; }
    public static string? CurrentVaultPath { get; private set; }
    public static string? CurrentMasterPassword { get; private set; }

    public static void ClearSession()
    {
        CurrentVault = null;
        CurrentVaultPath = null;
        CurrentMasterPassword = null;
    }

    public static void ClearSessionAndHelloCache()
    {
        ClearSession();
        SessionPasswordCache.Clear();
    }

    [ObservableProperty]
    public partial string VaultPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MasterPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? PasswordError { get; set; }

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

    public LoginViewModel(VaultService vaultService, AppSettings settings)
    {
        _vaultService = vaultService;
        _settings = settings;

        Directory.CreateDirectory(_settings.DefaultVaultDirectory);
        VaultPath = Path.Combine(_settings.DefaultVaultDirectory, "vault.pejpass");

        if (!string.IsNullOrEmpty(_settings.LastVaultPath) && File.Exists(_settings.LastVaultPath))
            VaultPath = _settings.LastVaultPath;
    }

    partial void OnIsCreateModeChanged(bool value)
    {
        if (value) IsOpenMode = false;
        _ = RefreshWindowsHelloVisibilityAsync();
        UpdateMasterPasswordStrength();
    }

    partial void OnIsOpenModeChanged(bool value)
    {
        if (value) IsCreateMode = false;
        _ = RefreshWindowsHelloVisibilityAsync();
    }

    partial void OnVaultPathChanged(string value) => _ = RefreshWindowsHelloVisibilityAsync();

    partial void OnMasterPasswordChanged(string value) => UpdateMasterPasswordStrength();

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
        ShowMasterPasswordStrength = level != PasswordStrengthLevel.Empty;
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
                InitialDirectory = _settings.DefaultVaultDirectory,
                OverwritePrompt = true
            };
            if (dlg.ShowDialog() == true)
                VaultPath = dlg.FileName;
        }
        else
        {
            var dlg = new OpenFileDialog
            {
                Filter = "PejPass Vault (*.pejpass)|*.pejpass",
                InitialDirectory = _settings.DefaultVaultDirectory
            };
            if (dlg.ShowDialog() == true)
                VaultPath = dlg.FileName;
        }
    }

    [RelayCommand]
    private async Task UnlockWithHelloAsync()
    {
        PasswordError = null;
        StatusMessage = string.Empty;

        if (!IsOpenMode)
        {
            PasswordError = "Windows Hello is only for opening an existing vault.";
            return;
        }

        if (string.IsNullOrWhiteSpace(VaultPath) || !File.Exists(VaultPath))
        {
            PasswordError = "Select an existing vault file first.";
            return;
        }

        if (!SessionPasswordCache.HasCacheFor(VaultPath))
        {
            PasswordError = "Unlock once with your master password first, then Windows Hello will be available until you exit the app.";
            return;
        }

        var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? System.Windows.Application.Current?.MainWindow;
        if (owner is null)
            return;

        IsBusy = true;
        try
        {
            StatusMessage = "Waiting for Windows Hello…";
            var verified = await WindowsHelloHelper.VerifyAsync(owner, "Unlock PejPass");
            if (!verified)
            {
                PasswordError = "Windows Hello verification failed or was cancelled.";
                StatusMessage = string.Empty;
                return;
            }

            if (!SessionPasswordCache.TryRestore(VaultPath, out var password))
            {
                PasswordError = "Could not restore the session. Enter your master password.";
                StatusMessage = string.Empty;
                return;
            }

            StatusMessage = "Unlocking vault…";
            var vault = await _vaultService.OpenVaultAsync(VaultPath, password);

            CurrentVault = vault;
            CurrentVaultPath = VaultPath;
            CurrentMasterPassword = password;

            SessionPasswordCache.Store(VaultPath, password);

            _settings.LastVaultPath = VaultPath;
            SettingsStore.Save(_settings);

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
        PasswordError = null;
        StatusMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(VaultPath))
        {
            PasswordError = "Please select a vault path.";
            return;
        }

        if (IsCreateMode)
        {
            var validation = MasterPasswordPolicy.Validate(MasterPassword);
            if (!validation.IsValid)
            {
                PasswordError = validation.ErrorMessage;
                return;
            }

            if (File.Exists(VaultPath))
            {
                PasswordError =
                    "A vault already exists here.\n" +
                    "Switch to «Open vault», or pick another path.";
                return;
            }
        }
        else
        {
            if (!File.Exists(VaultPath))
            {
                PasswordError =
                    "No vault at this path.\n" +
                    "Use «Create vault» or Browse to an existing file.";
                return;
            }
        }

        IsBusy = true;
        try
        {
            Vault vault;

            if (IsCreateMode)
            {
                StatusMessage = "Creating vault (Argon2id key derivation may take a moment)...";
                vault = await _vaultService.CreateVaultAsync(VaultPath, MasterPassword);
                StatusMessage = "Vault created successfully.";
            }
            else
            {
                StatusMessage = "Unlocking vault...";
                vault = await _vaultService.OpenVaultAsync(VaultPath, MasterPassword);
                StatusMessage = "Vault unlocked.";
            }

            CurrentVault = vault;
            CurrentVaultPath = VaultPath;
            CurrentMasterPassword = MasterPassword;

            if (_settings.WindowsHelloEnabled)
                SessionPasswordCache.Store(VaultPath, MasterPassword);
            else
                SessionPasswordCache.Clear();

            _settings.LastVaultPath = VaultPath;
            SettingsStore.Save(_settings);

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
