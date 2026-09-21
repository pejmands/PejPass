using System.Windows;
using Microsoft.Win32;
using PejPass.Domain.Settings;

namespace PejPass.Wpf.Services;

public sealed class ThemeService
{
    private readonly AppSettings _settings;
    private ResourceDictionary? _currentTheme;

    public ThemeService(AppSettings settings)
    {
        _settings = settings;
    }

    public void Apply()
    {
        var useDark = _settings.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => IsSystemDark()
        };

        var uri = useDark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        var dict = new ResourceDictionary { Source = uri };

        var app = System.Windows.Application.Current;
        if (app is null) return;

        if (_currentTheme is not null)
            app.Resources.MergedDictionaries.Remove(_currentTheme);

        app.Resources.MergedDictionaries.Insert(0, dict);
        _currentTheme = dict;
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i)
                return i == 0;
        }
        catch
        {
            // fall through
        }

        return true; // default dark
    }
}
