using Microsoft.Win32;
using PejPass.Domain.Settings;
using System.Windows.Media;
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

        ApplyColors(useDark);
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

    private static void ApplyColors(bool dark)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var colors = dark
            ? new Dictionary<string, Color>
            {
                ["BgBrush"] = Color.FromRgb(0x1E, 0x1E, 0x2E),
                ["SurfaceBrush"] = Color.FromRgb(0x31, 0x32, 0x44),
                ["SurfaceAltBrush"] = Color.FromRgb(0x45, 0x47, 0x5A),
                ["SurfaceHoverBrush"] = Color.FromRgb(0x58, 0x5B, 0x70),
                ["SurfacePressedBrush"] = Color.FromRgb(0x6C, 0x70, 0x86),
                ["TextBrush"] = Color.FromRgb(0xCD, 0xD6, 0xF4),
                ["TextSecondaryBrush"] = Color.FromRgb(0xA6, 0xAD, 0xC8),
                ["TextDisabledBrush"] = Color.FromRgb(0x6C, 0x70, 0x86),
                ["MutedBrush"] = Color.FromRgb(0xA6, 0xAD, 0xC8),
                ["AccentBrush"] = Color.FromRgb(0x89, 0xB4, 0xFA),
                ["AccentHoverBrush"] = Color.FromRgb(0xA6, 0xC8, 0xFF),
                ["AccentPressedBrush"] = Color.FromRgb(0x74, 0xA8, 0xF8),
                ["DangerBrush"] = Color.FromRgb(0xF3, 0x8B, 0xA8),
                ["DangerHoverBrush"] = Color.FromRgb(0xF5, 0xA6, 0xBC),
                ["DangerPressedBrush"] = Color.FromRgb(0xE7, 0x78, 0x98),
                ["DangerSurfaceBrush"] = Color.FromRgb(0x4A, 0x26, 0x32),
                ["SuccessBrush"] = Color.FromRgb(0xA6, 0xE3, 0xA1),
                ["WarningBrush"] = Color.FromRgb(0xF9, 0xE2, 0xAF),
                ["ButtonTextBrush"] = Color.FromRgb(0x1E, 0x1E, 0x2E),
                ["BorderBrush"] = Color.FromRgb(0x45, 0x47, 0x5A),
                ["BorderSubtleBrush"] = Color.FromRgb(0x3A, 0x3B, 0x4D),
                ["BorderFocusBrush"] = Color.FromRgb(0x89, 0xB4, 0xFA),
                ["OverlayBrush"] = Color.FromArgb(0xCC, 0x1E, 0x1E, 0x2E),
                ["ScrollThumbBrush"] = Color.FromRgb(0x58, 0x5B, 0x70),
                ["ScrollTrackBrush"] = Color.FromRgb(0x31, 0x32, 0x44),
                ["ComboItemHoverBrush"] = Color.FromRgb(0x45, 0x47, 0x5A),
                ["ValidationErrorBrush"] = Color.FromRgb(0xF3, 0x8B, 0xA8),
            }
            : new Dictionary<string, Color>
            {
                ["BgBrush"] = Color.FromRgb(0xEF, 0xF1, 0xF5),
                ["SurfaceBrush"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
                ["SurfaceAltBrush"] = Color.FromRgb(0xE6, 0xE9, 0xEF),
                ["SurfaceHoverBrush"] = Color.FromRgb(0xDC, 0xE0, 0xE8),
                ["SurfacePressedBrush"] = Color.FromRgb(0xCC, 0xD0, 0xDA),
                ["TextBrush"] = Color.FromRgb(0x4C, 0x4F, 0x69),
                ["TextSecondaryBrush"] = Color.FromRgb(0x6C, 0x6F, 0x85),
                ["TextDisabledBrush"] = Color.FromRgb(0x9C, 0xA0, 0xB0),
                ["MutedBrush"] = Color.FromRgb(0x6C, 0x6F, 0x85),
                ["AccentBrush"] = Color.FromRgb(0x1E, 0x66, 0xF5),
                ["AccentHoverBrush"] = Color.FromRgb(0x1A, 0x5C, 0xDB),
                ["AccentPressedBrush"] = Color.FromRgb(0x17, 0x4F, 0xBE),
                ["DangerBrush"] = Color.FromRgb(0xD2, 0x0F, 0x39),
                ["DangerHoverBrush"] = Color.FromRgb(0xB8, 0x0D, 0x32),
                ["DangerPressedBrush"] = Color.FromRgb(0x9F, 0x0B, 0x2B),
                ["DangerSurfaceBrush"] = Color.FromRgb(0xFC, 0xE9, 0xED),
                ["SuccessBrush"] = Color.FromRgb(0x40, 0xA0, 0x2B),
                ["WarningBrush"] = Color.FromRgb(0xDF, 0x8E, 0x1D),
                ["ButtonTextBrush"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
                ["BorderBrush"] = Color.FromRgb(0xCC, 0xD0, 0xDA),
                ["BorderSubtleBrush"] = Color.FromRgb(0xDC, 0xE0, 0xE8),
                ["BorderFocusBrush"] = Color.FromRgb(0x1E, 0x66, 0xF5),
                ["OverlayBrush"] = Color.FromArgb(0xAA, 0xEF, 0xF1, 0xF5),
                ["ScrollThumbBrush"] = Color.FromRgb(0x9C, 0xA0, 0xB0),
                ["ScrollTrackBrush"] = Color.FromRgb(0xE6, 0xE9, 0xEF),
                ["ComboItemHoverBrush"] = Color.FromRgb(0xDC, 0xE0, 0xE8),
                ["ValidationErrorBrush"] = Color.FromRgb(0xD2, 0x0F, 0x39),
            };

        foreach (var (key, color) in colors)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            app.Resources[key] = brush;
        }

        // Soft per-window pulse so Dark↔Light doesn't feel like a hard cut
        UiPolish.OnThemeApplied();
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
