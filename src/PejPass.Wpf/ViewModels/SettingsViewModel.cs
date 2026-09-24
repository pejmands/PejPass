using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Application.Services;
using PejPass.Domain.Settings;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using PejPass.Wpf.Views;
using System.Windows;
using ThemeMode = PejPass.Domain.Settings.ThemeMode;

namespace PejPass.Wpf.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ThemeService _themeService;
    private readonly VaultService _vaultService;
    private readonly VaultSession _vaultSession;

    private readonly ThemeMode _savedTheme;
    private readonly int _savedAutoLock;
    private readonly int _savedClipboard;
    private readonly bool _savedWindowsHello;

    private readonly bool _suppressThemePreview;

    [ObservableProperty]
    public partial int AutoLockMinutes { get; set; }

    [ObservableProperty]
    public partial int ClipboardClearSeconds { get; set; }

    [ObservableProperty]
    public partial int SelectedThemeIndex { get; set; }

    [ObservableProperty]
    public partial bool WindowsHelloEnabled { get; set; }

    public string[] ThemeOptions { get; } = ["System", "Dark", "Light"];

    public event EventHandler? RequestClose;

    public SettingsViewModel(
        AppSettings settings,
        ThemeService themeService,
        VaultService vaultService,
        VaultSession vaultSession)
    {
        _settings = settings;
        _themeService = themeService;
        _vaultService = vaultService;
        _vaultSession = vaultSession;

        _savedTheme = settings.Theme;
        _savedAutoLock = settings.AutoLockMinutes;
        _savedClipboard = settings.ClipboardClearSeconds;
        _savedWindowsHello = settings.WindowsHelloEnabled;

        _suppressThemePreview = true;
        AutoLockMinutes = settings.AutoLockMinutes;
        ClipboardClearSeconds = settings.ClipboardClearSeconds;
        SelectedThemeIndex = (int)settings.Theme;
        WindowsHelloEnabled = settings.WindowsHelloEnabled;
        _suppressThemePreview = false;
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_suppressThemePreview) return;
        if (value < 0 || value > 2) return;
        ThemeService.Preview((ThemeMode)value);
    }

    [RelayCommand]
    private void Save()
    {
        if (AutoLockMinutes < 0) AutoLockMinutes = 0;
        if (ClipboardClearSeconds < 5) ClipboardClearSeconds = 5;
        if (ClipboardClearSeconds > 300) ClipboardClearSeconds = 300;

        _settings.AutoLockMinutes = AutoLockMinutes;
        _settings.ClipboardClearSeconds = ClipboardClearSeconds;
        _settings.Theme = (ThemeMode)SelectedThemeIndex;
        _settings.WindowsHelloEnabled = WindowsHelloEnabled;

        if (!WindowsHelloEnabled)
            SessionPasswordCache.Clear();

        SettingsStore.Save(_settings);
        _themeService.Apply();

        DialogService.Success("Settings saved.", "Settings");
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    public void RevertPreview()
    {
        _settings.Theme = _savedTheme;
        _settings.AutoLockMinutes = _savedAutoLock;
        _settings.ClipboardClearSeconds = _savedClipboard;
        _settings.WindowsHelloEnabled = _savedWindowsHello;
        _themeService.Apply();
    }

    [RelayCommand]
    private void ChangeMasterPassword()
    {
        if (!_vaultSession.IsActive)
        {
            DialogService.Warning("Open a vault first to change the master password.", "Change password");
            return;
        }

        var owner = System.Windows.Application.Current?.Windows.OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? System.Windows.Application.Current?.MainWindow;

        var vm = new ChangeMasterPasswordViewModel(
            _vaultService,
            _vaultSession);

        var win = new ChangeMasterPasswordWindow(vm) { Owner = owner };
        if (win.ShowDialog() == true)
        {
            DialogService.Info(
                "Master password changed successfully.\n\nThe vault is now encrypted with the new password.",
                "Password changed");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RevertPreview();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
