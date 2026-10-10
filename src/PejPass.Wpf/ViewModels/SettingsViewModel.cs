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

/// <summary>Registers or removes the Windows startup entry.</summary>
public delegate bool StartupRegistrar(bool enabled, out string? error);

public partial class SettingsViewModel : ObservableObject
{
    private const int MaxAutoLockMinutes = 1440;

    private readonly AppSettings _settings;
    private readonly ThemeService _themeService;
    private readonly VaultService _vaultService;
    private readonly VaultSession _vaultSession;
    private readonly SessionPasswordCache _sessionPasswordCache;
    private readonly Action<string, string> _showError;
    private readonly StartupRegistrar _setStartup;

    private readonly ThemeMode _savedTheme;
    private readonly int _savedAutoLock;
    private readonly int _savedClipboard;
    private readonly int _savedRevealSecret;
    private readonly bool _savedWindowsHello;
    private readonly int _savedWindowsHelloTimeout;
    private readonly FontSizeMode _savedFontSize;
    private readonly double _savedZoom;
    private readonly bool _savedOnlineFaviconFetching;
    private readonly bool _savedAutoCheckForUpdates;
    private readonly bool _savedMinimizeToSystemTray;
    private readonly bool _savedCloseToSystemTray;
    private readonly bool _savedStartWithWindows;

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
    public partial int SelectedWindowsHelloTimeoutIndex { get; set; }

    [ObservableProperty]
    public partial bool OnlineFaviconFetchingEnabled { get; set; }

    [ObservableProperty]
    public partial bool AutoCheckForUpdates { get; set; }

    [ObservableProperty]
    public partial bool MinimizeToSystemTray { get; set; }

    [ObservableProperty]
    public partial bool CloseToSystemTray { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

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

    public string[] WindowsHelloTimeoutOptions { get; } = ["15 minutes", "1 hour", "4 hours (recommended)", "Until app closes"];

    public int[] WindowsHelloTimeoutValues { get; } = [15, 60, 240, 0];

    /// <summary>
    /// When true (login screen), only appearance options are shown and saved. Startup,
    /// tray, update, and vault/security settings stay hidden and are not
    /// written back on Save.
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
        VaultSession vaultSession,
        SessionPasswordCache sessionPasswordCache,
        Action<string, string>? showError = null,
        StartupRegistrar? setStartup = null)
    {
        _settings = settings;
        _themeService = themeService;
        _vaultService = vaultService;
        _vaultSession = vaultSession;
        _sessionPasswordCache = sessionPasswordCache;
        _showError = showError ?? DialogService.Error;
        _setStartup = setStartup ?? WindowsStartupService.TrySetEnabled;

        _savedTheme = settings.Theme;
        _savedAutoLock = settings.AutoLockMinutes;
        _savedClipboard = settings.ClipboardClearSeconds;
        _savedRevealSecret = settings.RevealSecretSeconds;
        _savedWindowsHello = settings.WindowsHelloEnabled;
        _savedWindowsHelloTimeout = settings.WindowsHelloTimeoutMinutes;
        _savedFontSize = settings.FontSize;
        _savedZoom = settings.Zoom;
        _savedOnlineFaviconFetching = settings.OnlineFaviconFetchingEnabled;
        _savedAutoCheckForUpdates = settings.AutoCheckForUpdates;
        _savedMinimizeToSystemTray = settings.MinimizeToSystemTray;
        _savedCloseToSystemTray = settings.CloseToSystemTray;
        _savedStartWithWindows = settings.StartWithWindows;

        _suppressThemePreview = true;
        _suppressZoomPreview = true;
        AutoLockMinutes = settings.AutoLockMinutes;
        ClipboardClearSeconds = settings.ClipboardClearSeconds;
        RevealSecretSeconds = settings.RevealSecretSeconds;
        SelectedThemeIndex = (int)settings.Theme;
        WindowsHelloEnabled = settings.WindowsHelloEnabled;
        SelectedWindowsHelloTimeoutIndex = Array.IndexOf(WindowsHelloTimeoutValues, settings.WindowsHelloTimeoutMinutes);
        if (SelectedWindowsHelloTimeoutIndex < 0) SelectedWindowsHelloTimeoutIndex = 2;
        OnlineFaviconFetchingEnabled = settings.OnlineFaviconFetchingEnabled;
        AutoCheckForUpdates = settings.AutoCheckForUpdates;
        MinimizeToSystemTray = settings.MinimizeToSystemTray;
        CloseToSystemTray = settings.CloseToSystemTray;
        StartWithWindows = settings.StartWithWindows;
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
        _themeService.Preview((ThemeMode)value);
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
        catch (Exception)
        {
            RevertPreview();

            _showError(
                "The settings cannot be modified right now. Check file permissions and try again.",
                "Settings unavailable");

            return;
        }

        // Change the registry first, and only when the requested value differs.
        // This keeps the settings object and file untouched if registration fails.
        var startupChanged = !AppearanceOnly && StartWithWindows != _savedStartWithWindows;

        if (startupChanged)
        {
            string? startupError;
            bool startupUpdated;

            try
            {
                startupUpdated = _setStartup(StartWithWindows, out startupError);
            }
            catch (Exception)
            {
                startupUpdated = false;
                startupError = "The startup registrar failed unexpectedly.";
            }

            if (!startupUpdated)
            {
                // Reset only this option; keep the user's other edits so they can retry.
                StartWithWindows = _savedStartWithWindows;

                _showError(
                    $"Windows startup could not be updated.\n\n{startupError}",
                    "Startup setting unavailable");

                return;
            }
        }

        // Apply the editable values to memory only after startup registration succeeds.
        _settings.Theme = (ThemeMode)SelectedThemeIndex;
        _settings.FontSize = (FontSizeMode)SelectedFontSizeIndex;
        _settings.Zoom = ZoomBehavior.ZoomLevels[SelectedZoomIndex];

        // The tray icon is always available. This setting only controls
        // whether closing or minimizing the main window hides it to the tray.
        System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!AppearanceOnly)
        {
            _settings.AutoCheckForUpdates = AutoCheckForUpdates;
            _settings.MinimizeToSystemTray = MinimizeToSystemTray;
            _settings.CloseToSystemTray = CloseToSystemTray;
            _settings.StartWithWindows = StartWithWindows;
            _settings.AutoLockMinutes = AutoLockMinutes;
            _settings.ClipboardClearSeconds = ClipboardClearSeconds;
            _settings.RevealSecretSeconds = RevealSecretSeconds;
            _settings.WindowsHelloEnabled = WindowsHelloEnabled;
            _settings.WindowsHelloTimeoutMinutes = WindowsHelloTimeoutValues[SelectedWindowsHelloTimeoutIndex];
            _settings.OnlineFaviconFetchingEnabled = OnlineFaviconFetchingEnabled;
        }

        // If persistence fails, attempt to undo the external registry change.
        if (!SettingsStore.TrySave(_settings))
        {
            var startupRollbackSucceeded = true;

            if (startupChanged)
            {
                try
                {
                    startupRollbackSucceeded =
                        _setStartup(_savedStartWithWindows, out _);
                }
                catch (Exception)
                {
                    startupRollbackSucceeded = false;
                }
            }

            RevertPreview();

            _showError(
                startupRollbackSucceeded
                    ? "Failed to save settings. The changes were not applied."
                    : "Failed to save settings, and the Windows startup setting could not be rolled back. Check the Windows startup setting manually.",
                "Save failed");

            return;
        }

        if (!AppearanceOnly)
        {
            if (!WindowsHelloEnabled)
                _sessionPasswordCache.Clear();
            else
                _sessionPasswordCache.RefreshExpiry();
        }

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
        _settings.WindowsHelloTimeoutMinutes = _savedWindowsHelloTimeout;
        _settings.FontSize = _savedFontSize;
        _settings.Zoom = _savedZoom;
        _settings.OnlineFaviconFetchingEnabled = _savedOnlineFaviconFetching;
        _settings.AutoCheckForUpdates = _savedAutoCheckForUpdates;
        _settings.MinimizeToSystemTray = _savedMinimizeToSystemTray;
        _settings.CloseToSystemTray = _savedCloseToSystemTray;
        _settings.StartWithWindows = _savedStartWithWindows;

        _suppressThemePreview = true;
        try
        {
            AutoLockMinutes = _savedAutoLock;
            ClipboardClearSeconds = _savedClipboard;
            RevealSecretSeconds = _savedRevealSecret;
            SelectedThemeIndex = (int)_savedTheme;
            WindowsHelloEnabled = _savedWindowsHello;
            SelectedWindowsHelloTimeoutIndex = Array.IndexOf(WindowsHelloTimeoutValues, _savedWindowsHelloTimeout);
            if (SelectedWindowsHelloTimeoutIndex < 0) SelectedWindowsHelloTimeoutIndex = 2;
            OnlineFaviconFetchingEnabled = _savedOnlineFaviconFetching;
            AutoCheckForUpdates = _savedAutoCheckForUpdates;
            MinimizeToSystemTray = _savedMinimizeToSystemTray;
            CloseToSystemTray = _savedCloseToSystemTray;
            StartWithWindows = _savedStartWithWindows;
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
        if (!_vaultSession.IsActive)
        {
            DialogService.Warning(
                "Open a vault first to change the master password.",
                "Change password");
            return;
        }

        var owner = System.Windows.Application.Current?.Windows.OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? System.Windows.Application.Current?.MainWindow;

        var vm = new ChangeMasterPasswordViewModel(
            _vaultService,
            _vaultSession,
            _sessionPasswordCache);

        var win = new ChangeMasterPasswordWindow(vm) { Owner = owner };
        if (win.ShowDialog() == true)
        {
            DialogService.Info(
                "Master password changed successfully.\n\nThe vault is now encrypted with the new password.",
                "Password changed");
        }
    }

    [RelayCommand]
    private static void ClearFaviconCache()
    {
        try
        {
            FaviconService.ClearCache();
            SnackbarService.Show("Favicon cache cleared.");
        }
        catch (Exception)
        {
            DialogService.Error("Could not clear the cache. Please try again.", "Clear cache failed");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RevertPreview();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
