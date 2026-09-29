using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Application.Services;
using PejPass.Domain.Settings;
using PejPass.Wpf.Controls;
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
    private readonly FontSizeMode _savedFontSize;
    private readonly double _savedZoom;

    private bool _suppressThemePreview;
    private bool _suppressZoomPreview;

    [ObservableProperty]
    public partial int AutoLockMinutes { get; set; }

    [ObservableProperty]
    public partial int ClipboardClearSeconds { get; set; }

    [ObservableProperty]
    public partial string? AutoLockError { get; set; }

    [ObservableProperty]
    public partial string? ClipboardError { get; set; }

    [ObservableProperty]
    public partial int SelectedThemeIndex { get; set; }

    [ObservableProperty]
    public partial bool WindowsHelloEnabled { get; set; }

    [ObservableProperty]
    public partial int SelectedFontSizeIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedZoomIndex { get; set; }

    public string[] ThemeOptions { get; } = ["System", "Dark", "Light"];

    public string[] FontSizeOptions { get; } = ["Small", "Medium", "Large"];

    public string[] ZoomOptions { get; } = ["80%", "90%", "100%", "110%", "120%", "130%", "140%"];

    public event EventHandler? RequestClose;
    public event EventHandler? ValidationFailed;

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
        _savedFontSize = settings.FontSize;
        _savedZoom = settings.Zoom;

        _suppressThemePreview = true;
        AutoLockMinutes = settings.AutoLockMinutes;
        ClipboardClearSeconds = settings.ClipboardClearSeconds;
        SelectedThemeIndex = (int)settings.Theme;
        WindowsHelloEnabled = settings.WindowsHelloEnabled;
        SelectedFontSizeIndex = (int)settings.FontSize;
        SelectedZoomIndex = GetZoomIndex(settings.Zoom);
        _suppressThemePreview = false;
    }

    partial void OnAutoLockMinutesChanged(int value) => ValidateAutoLock();

    partial void OnClipboardClearSecondsChanged(int value) => ValidateClipboard();

    private void ValidateAutoLock()
    {
        AutoLockError = AutoLockMinutes < 0
            ? "Auto-lock cannot be negative."
            : null;
    }

    private void ValidateClipboard()
    {
        ClipboardError = ClipboardClearSeconds switch
        {
            < 5 => "Clipboard timeout must be at least 5 seconds.",
            > 300 => "Clipboard timeout cannot exceed 300 seconds.",
            _ => null
        };
    }

    partial void OnSelectedFontSizeIndexChanged(int value)
    {
        if (value < 0 || value > 2) return;
        App.ApplyFontSize((FontSizeMode)value);
    }

    partial void OnSelectedZoomIndexChanged(int value)
    {
        if (_suppressZoomPreview) return;
        if (value < 0 || value >= ZoomOptions.Length) return;
        ZoomBehavior.SetGlobalZoom(ZoomBehavior.ZoomLevels[value]);
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
        Validate();

        if (!string.IsNullOrEmpty(AutoLockError) ||
            !string.IsNullOrEmpty(ClipboardError))
        {
            ValidationFailed?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            SettingsStore.EnsureWritable();
        }
        catch (Exception ex)
        {
            RevertPreview();

            DialogService.Error(
                $"The settings cannot be modified right now.\n\n{ex.Message}",
                "Settings unavailable");

            return;
        }

        _settings.AutoLockMinutes = AutoLockMinutes;
        _settings.ClipboardClearSeconds = ClipboardClearSeconds;
        _settings.Theme = (ThemeMode)SelectedThemeIndex;
        _settings.WindowsHelloEnabled = WindowsHelloEnabled;
        _settings.FontSize = (FontSizeMode)SelectedFontSizeIndex;
        _settings.Zoom = ZoomBehavior.ZoomLevels[SelectedZoomIndex];

        if (!SettingsStore.TrySave(_settings))
        {
            RevertPreview();

            DialogService.Error(
                "Failed to save settings. The changes were not applied.",
                "Save failed");

            return;
        }

        if (!WindowsHelloEnabled)
            SessionPasswordCache.Clear();

        _themeService.Apply();

        RequestClose?.Invoke(this, EventArgs.Empty);
        SnackbarService.Show("Settings saved.");
    }

    private void Validate()
    {
        ValidateAutoLock();
        ValidateClipboard();
    }

    public void RevertPreview()
    {
        _settings.Theme = _savedTheme;
        _settings.AutoLockMinutes = _savedAutoLock;
        _settings.ClipboardClearSeconds = _savedClipboard;
        _settings.WindowsHelloEnabled = _savedWindowsHello;
        _settings.FontSize = _savedFontSize;
        _settings.Zoom = _savedZoom;

        _suppressThemePreview = true;
        try
        {
            AutoLockMinutes = _savedAutoLock;
            ClipboardClearSeconds = _savedClipboard;
            SelectedThemeIndex = (int)_savedTheme;
            WindowsHelloEnabled = _savedWindowsHello;
            SelectedFontSizeIndex = (int)_savedFontSize;

            _suppressZoomPreview = true;
            try
            {
                SelectedZoomIndex = GetZoomIndex(_savedZoom);
            }
            finally
            {
                _suppressZoomPreview = false;
            }
        }
        finally
        {
            _suppressThemePreview = false;
        }

        _themeService.Apply();
        App.ApplyFontSize(_savedFontSize);
        ZoomBehavior.SetGlobalZoom(_savedZoom);
    }

    public void UpdateZoomFromGlobal(double zoom)
    {
        _suppressZoomPreview = true;
        try
        {
            SelectedZoomIndex = GetZoomIndex(zoom);
        }
        finally
        {
            _suppressZoomPreview = false;
        }
    }

    private static int GetZoomIndex(double zoom)
    {
        var index = Array.FindIndex(
            ZoomBehavior.ZoomLevels,
            level => Math.Abs(level - zoom) < 0.001);

        return index >= 0 ? index : 2;
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
