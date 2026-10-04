using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Application.Services;
using PejPass.Domain.Settings;
using PejPass.Wpf.Controls;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using PejPass.Wpf.Views;
using System.IO;
using System.Windows;
using ThemeMode = PejPass.Domain.Settings.ThemeMode;

namespace PejPass.Wpf.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const int MaxAutoLockMinutes = 1440;

    private readonly AppSettings _settings;
    private readonly ThemeService _themeService;
    private readonly VaultService _vaultService;
    private readonly VaultSession _vaultSession;

    private readonly ThemeMode _savedTheme;
    private readonly int _savedAutoLock;
    private readonly int _savedClipboard;
    private readonly int _savedRevealSecret;
    private readonly bool _savedWindowsHello;
    private readonly FontSizeMode _savedFontSize;
    private readonly double _savedZoom;
    private readonly bool _savedOnlineFaviconFetching;

    private bool _suppressThemePreview;
    private bool _suppressZoomPreview;

    [ObservableProperty]
    public partial int AutoLockMinutes { get; set; }

    [ObservableProperty]
    public partial int ClipboardClearSeconds { get; set; }

    [ObservableProperty]
    public partial int RevealSecretSeconds { get; set; }

    [ObservableProperty]
    public partial string? AutoLockError { get; set; }

    [ObservableProperty]
    public partial string? ClipboardError { get; set; }

    [ObservableProperty]
    public partial int SelectedThemeIndex { get; set; }

    [ObservableProperty]
    public partial bool WindowsHelloEnabled { get; set; }

    [ObservableProperty]
    public partial bool OnlineFaviconFetchingEnabled { get; set; }

    [ObservableProperty]
    public partial int SelectedFontSizeIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedZoomIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedRevealSecretIndex { get; set; }

    public string[] ThemeOptions { get; } = ["System", "Dark", "Light"];

    public string[] FontSizeOptions { get; } = ["Small", "Medium", "Large"];

    public string[] ZoomOptions { get; } = ["80%", "90%", "100%", "110%", "120%", "130%", "140%"];

    public string[] RevealSecretOptions { get; } = ["Never", "5 seconds", "10 seconds", "30 seconds", "1 minute"];

    public int[] RevealSecretValues { get; } = [0, 5, 10, 30, 60];

    /// <summary>
    /// When true (login screen), only Theme / Font / Zoom are shown and saved.
    /// </summary>
    public bool AppearanceOnly { get; private set; }

    public bool ShowAdvancedSettings => !AppearanceOnly;

    public void ConfigureAppearanceOnly(bool appearanceOnly = true)
    {
        AppearanceOnly = appearanceOnly;
        OnPropertyChanged(nameof(AppearanceOnly));
        OnPropertyChanged(nameof(ShowAdvancedSettings));
    }

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
        _savedRevealSecret = settings.RevealSecretSeconds;
        _savedWindowsHello = settings.WindowsHelloEnabled;
        _savedFontSize = settings.FontSize;
        _savedZoom = settings.Zoom;
        _savedOnlineFaviconFetching = settings.OnlineFaviconFetchingEnabled;

        _suppressThemePreview = true;
        _suppressZoomPreview = true;
        AutoLockMinutes = settings.AutoLockMinutes;
        ClipboardClearSeconds = settings.ClipboardClearSeconds;
        RevealSecretSeconds = settings.RevealSecretSeconds;
        SelectedThemeIndex = (int)settings.Theme;
        WindowsHelloEnabled = settings.WindowsHelloEnabled;
        OnlineFaviconFetchingEnabled = settings.OnlineFaviconFetchingEnabled;
        SelectedFontSizeIndex = (int)settings.FontSize;
        SelectedZoomIndex = GetZoomIndex(settings.Zoom);
        SelectedRevealSecretIndex = GetRevealSecretIndex(settings.RevealSecretSeconds);
        _suppressZoomPreview = false;
        _suppressThemePreview = false;
    }

    partial void OnAutoLockMinutesChanged(int value) => ValidateAutoLock();

    partial void OnClipboardClearSecondsChanged(int value) => ValidateClipboard();

    private void ValidateAutoLock()
    {
        AutoLockError = AutoLockMinutes switch
        {
            < 0 => "Auto-lock cannot be negative.",
            > MaxAutoLockMinutes => "Auto-lock cannot exceed 24 hours.",
            _ => null
        };
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
        if (value < 0 || value >= ZoomBehavior.ZoomLevels.Length) return;
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
        if (!AppearanceOnly)
        {
            Validate();

            if (!string.IsNullOrEmpty(AutoLockError) ||
                !string.IsNullOrEmpty(ClipboardError))
            {
                ValidationFailed?.Invoke(this, EventArgs.Empty);
                return;
            }
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

        _settings.Theme = (ThemeMode)SelectedThemeIndex;
        _settings.FontSize = (FontSizeMode)SelectedFontSizeIndex;
        _settings.Zoom = ZoomBehavior.ZoomLevels[SelectedZoomIndex];

        if (!AppearanceOnly)
        {
            _settings.AutoLockMinutes = AutoLockMinutes;
            _settings.ClipboardClearSeconds = ClipboardClearSeconds;
            _settings.RevealSecretSeconds = RevealSecretSeconds;
            _settings.WindowsHelloEnabled = WindowsHelloEnabled;
            _settings.OnlineFaviconFetchingEnabled = OnlineFaviconFetchingEnabled;
        }

        if (!SettingsStore.TrySave(_settings))
        {
            RevertPreview();

            DialogService.Error(
                "Failed to save settings. The changes were not applied.",
                "Save failed");

            return;
        }

        if (!AppearanceOnly && !WindowsHelloEnabled)
            SessionPasswordCache.Clear();

        _themeService.Apply();
        if (!AppearanceOnly)
            FaviconService.ConfigureOnlineFetching(OnlineFaviconFetchingEnabled);

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
        _settings.RevealSecretSeconds = _savedRevealSecret;
        _settings.WindowsHelloEnabled = _savedWindowsHello;
        _settings.FontSize = _savedFontSize;
        _settings.Zoom = _savedZoom;
        _settings.OnlineFaviconFetchingEnabled = _savedOnlineFaviconFetching;

        _suppressThemePreview = true;
        try
        {
            AutoLockMinutes = _savedAutoLock;
            ClipboardClearSeconds = _savedClipboard;
            RevealSecretSeconds = _savedRevealSecret;
            SelectedThemeIndex = (int)_savedTheme;
            WindowsHelloEnabled = _savedWindowsHello;
            OnlineFaviconFetchingEnabled = _savedOnlineFaviconFetching;
            SelectedFontSizeIndex = (int)_savedFontSize;
            SelectedRevealSecretIndex = GetRevealSecretIndex(_savedRevealSecret);

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
        var levels = ZoomBehavior.ZoomLevels;
        var best = 2;
        var bestDiff = double.MaxValue;
        for (var i = 0; i < levels.Length; i++)
        {
            var diff = Math.Abs(levels[i] - zoom);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = i;
            }
        }
        return best;
    }

    private int GetRevealSecretIndex(int seconds)
    {
        for (var i = 0; i < RevealSecretValues.Length; i++)
        {
            if (RevealSecretValues[i] == seconds)
                return i;
        }
        return 0;
    }

    [RelayCommand]
    private void ChangeMasterPassword()
    {
        if (!_vaultSession.IsUnlocked)
        {
            DialogService.Info(
                "Unlock the vault before changing the master password.",
                "Vault locked");
            return;
        }

        var window = new ChangeMasterPasswordWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private void ClearFaviconCache()
    {
        try
        {
            FaviconService.ClearCache();
            SnackbarService.Show("Favicon cache cleared.");
        }
        catch (Exception ex)
        {
            DialogService.Error(ex.Message, "Clear cache failed");
        }
    }
}
