using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Domain.Policies;
using PejPass.Domain.Settings;
using System.IO;

namespace PejPass.Wpf.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly VaultService _vaultService;
    private readonly AppSettings _settings;

    public event EventHandler? RequestClose;

    // Shared session state after successful unlock
    public static Vault? CurrentVault { get; private set; }
    public static string? CurrentVaultPath { get; private set; }
    public static string? CurrentMasterPassword { get; private set; }

    /// <summary>
    /// Clears all sensitive session data. Call this when locking the vault.
    /// </summary>
    public static void ClearSession()
    {
        CurrentVault = null;
        CurrentVaultPath = null;
        CurrentMasterPassword = null;
    }

    [ObservableProperty] private string _vaultPath = string.Empty;
    [ObservableProperty] private string _masterPassword = string.Empty;
    [ObservableProperty] private string? _passwordError;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isOpenMode = true;
    [ObservableProperty] private bool _isCreateMode;
    [ObservableProperty] private bool _isBusy;

    public LoginViewModel(VaultService vaultService, AppSettings settings)
    {
        _vaultService = vaultService;
        _settings = settings;

        // Default path
        Directory.CreateDirectory(_settings.DefaultVaultDirectory);
        VaultPath = Path.Combine(_settings.DefaultVaultDirectory, "vault.pejpass");

        if (!string.IsNullOrEmpty(_settings.LastVaultPath) && File.Exists(_settings.LastVaultPath))
            VaultPath = _settings.LastVaultPath;
    }

    partial void OnIsCreateModeChanged(bool value)
    {
        if (value) IsOpenMode = false;
    }

    partial void OnIsOpenModeChanged(bool value)
    {
        if (value) IsCreateMode = false;
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
                InitialDirectory = _settings.DefaultVaultDirectory
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

            // Store session state
            CurrentVault = vault;
            CurrentVaultPath = VaultPath;
            CurrentMasterPassword = MasterPassword;

            _settings.LastVaultPath = VaultPath;

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
