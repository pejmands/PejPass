using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Application.Services;
using PejPass.Domain.Policies;
using PejPass.Domain.Security;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.ViewModels;

public partial class ChangeMasterPasswordViewModel(
    VaultService vaultService,
    VaultSession vaultSession) : ObservableObject
{
    private readonly VaultService _vaultService = vaultService;
    private readonly VaultSession _vaultSession = vaultSession;

    [ObservableProperty]
    public partial string CurrentPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? CurrentPasswordError { get; set; }

    [ObservableProperty]
    public partial string? NewPasswordError { get; set; }

    [ObservableProperty]
    public partial string? ConfirmPasswordError { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string NewPasswordStrengthLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double NewPasswordStrengthProgress { get; set; }

    [ObservableProperty]
    public partial int NewPasswordStrengthLevel { get; set; }

    [ObservableProperty]
    public partial bool ShowNewPasswordStrength { get; set; }

    public event EventHandler? RequestClose;

    public bool Success { get; private set; }

    partial void OnNewPasswordChanged(string value) => UpdateNewPasswordStrength();

    private void UpdateNewPasswordStrength()
    {
        if (string.IsNullOrEmpty(NewPassword))
        {
            ShowNewPasswordStrength = false;
            NewPasswordStrengthLabel = string.Empty;
            NewPasswordStrengthProgress = 0;
            NewPasswordStrengthLevel = 0;
            return;
        }

        var level = PasswordStrength.Evaluate(NewPassword);
        NewPasswordStrengthLevel = (int)level;
        NewPasswordStrengthLabel = PasswordStrength.GetLabel(level);
        NewPasswordStrengthProgress = PasswordStrength.GetProgress(level);
        ShowNewPasswordStrength = level != PasswordStrengthLevel.Empty;
    }

    [RelayCommand]
    private async Task ChangeAsync()
    {
        CurrentPasswordError = null;
        NewPasswordError = null;
        ConfirmPasswordError = null;
        Success = false;

        var path = _vaultSession.VaultPath;
        var vault = _vaultSession.Vault;

        if (string.IsNullOrEmpty(path) || vault is null)
        {
            CurrentPasswordError = "No vault is open.";
            return;
        }

        if (string.IsNullOrEmpty(CurrentPassword))
        {
            CurrentPasswordError = "Enter your current master password.";
            return;
        }

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            ConfirmPasswordError = "New password and confirmation do not match.";
            return;
        }

        var validation = MasterPasswordPolicy.Validate(NewPassword);
        if (!validation.IsValid)
        {
            NewPasswordError = validation.ErrorMessage;
            return;
        }

        IsBusy = true;
        try
        {
            await _vaultService.ChangeMasterPasswordAsync(
                path,
                CurrentPassword,
                NewPassword,
                vault);

            _vaultSession.UpdateSecret(NewPassword);
            SessionPasswordCache.Store(path, NewPassword);

            Success = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (msg.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("tag", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("decrypt", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("padding", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("mac", StringComparison.OrdinalIgnoreCase))
            {
                CurrentPasswordError = "Current master password is incorrect.";
            }
            else
            {
                CurrentPasswordError = msg;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Success = false;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
