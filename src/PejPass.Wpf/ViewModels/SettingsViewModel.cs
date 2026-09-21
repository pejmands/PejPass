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

    [ObservableProperty] private int _autoLockMinutes;
    [ObservableProperty] private int _clipboardClearSeconds;
    [ObservableProperty] private int _selectedThemeIndex; // 0=System, 1=Dark, 2=Light

    public string[] ThemeOptions { get; } = ["System", "Dark", "Light"];

    public event EventHandler? RequestClose;

    public SettingsViewModel(AppSettings settings, ThemeService themeService)
    {
        _settings = settings;
        _themeService = themeService;

        AutoLockMinutes = settings.AutoLockMinutes;
        ClipboardClearSeconds = settings.ClipboardClearSeconds;
        SelectedThemeIndex = (int)settings.Theme;
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (value < 0 || value > 2) return;
        _settings.Theme = (ThemeMode)value;
        _themeService.Apply();
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

    [RelayCommand]
    private void Cancel()
    {
        // Revert theme preview if cancelled
        _themeService.Apply();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
