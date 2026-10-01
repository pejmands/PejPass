using Microsoft.Win32;
using PejPass.Domain.Settings;
using ThemeMode = PejPass.Domain.Settings.ThemeMode;

namespace PejPass.Wpf.Services;

/// <summary>
/// Applies theme brushes to Application.Resources and watches OS theme when mode is System.
/// </summary>
public sealed class ThemeService(AppSettings settings) : IDisposable
{
    private readonly AppSettings _settings = settings;
    private bool _watching;

    /// <summary>Apply the saved theme from settings.</summary>
    public void Apply()
    {
        ApplyMode(_settings.Theme);
        UpdateSystemWatch();
    }

    /// <summary>Preview a theme without writing it to settings (for Settings dialog live preview).</summary>
    public static void Preview(ThemeMode mode)
    {
        ApplyMode(mode);
    }

    private static void ApplyMode(ThemeMode mode)
    {
        var useDark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => IsSystemDark()
        };

        ApplyThemeDictionary(useDark);
    }

    private void UpdateSystemWatch()
    {
        if (_settings.Theme == ThemeMode.System)
            StartWatch();
        else
            StopWatch();
    }

    private void StartWatch()
    {
        if (_watching) return;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _watching = true;
    }

    private void StopWatch()
    {
        if (!_watching) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _watching = false;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color))
            return;

        if (_settings.Theme != ThemeMode.System)
            return;

        // Marshal to UI thread
        var app = System.Windows.Application.Current;
        if (app is null) return;

        app.Dispatcher.Invoke(() => ApplyMode(ThemeMode.System));
    }

    private static void ApplyThemeDictionary(bool dark)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var dictionaries = app.Resources.MergedDictionaries;
        var existingTheme = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase) == true);

        if (existingTheme is not null)
            dictionaries.Remove(existingTheme);

        var themeSource = dark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";
        dictionaries.Insert(0, new System.Windows.ResourceDictionary
        {
            Source = new Uri(themeSource, UriKind.Relative)
        });
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
            // ignore
        }

        return true;
    }

    public void Dispose()
    {
        StopWatch();
        GC.SuppressFinalize(this);
    }
}
