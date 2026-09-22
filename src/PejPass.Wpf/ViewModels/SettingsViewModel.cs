using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Settings;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ThemeService _themeService;

    private readonly ThemeMode _savedTheme;
    private readonly int _savedAutoLock;
    private readonly int _savedClipboard;

    private readonly bool _suppressThemePreview;

    [ObservableProperty] 
    public partial int AutoLockMinutes { get; set; }

    [ObservableProperty] 
    public partial int ClipboardClearSeconds { get; set; }

    [ObservableProperty] 
    public partial int SelectedThemeIndex { get; set; }

    public string[] ThemeOptions { get; } = ["System", "Dark", "Light"];

    public event EventHandler? RequestClose;

    public SettingsViewModel(AppSettings settings, ThemeService themeService)
    {
        _settings = settings;
        _themeService = themeService;

        _savedTheme = settings.Theme;
        _savedAutoLock = settings.AutoLockMinutes;
        _savedClipboard = settings.ClipboardClearSeconds;

        _suppressThemePreview = true;
        AutoLockMinutes = settings.AutoLockMinutes;
        ClipboardClearSeconds = settings.ClipboardClearSeconds;
        SelectedThemeIndex = (int)settings.Theme;
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

        SettingsStore.Save(_settings);
        _themeService.Apply();

        DialogService.Success("Settings saved.", "Settings");
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Restore saved values and theme (no close).</summary>
    public void RevertPreview()
    {
        _settings.Theme = _savedTheme;
        _settings.AutoLockMinutes = _savedAutoLock;
        _settings.ClipboardClearSeconds = _savedClipboard;
        _themeService.Apply();
    }

    [RelayCommand]
    private void Cancel()
    {
        RevertPreview();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
