using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PejPass.Domain.Settings;

namespace PejPass.Wpf.Services;

/// <summary>
/// Applies theme by writing brush keys directly into Application.Resources.
/// This is reliable with DynamicResource and avoids MergedDictionary lookup-order bugs.
/// </summary>
public sealed class ThemeService
{
    private readonly AppSettings _settings;

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

        ApplyColors(useDark);
    }

    private static void ApplyColors(bool dark)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        // Pack colors as (R,G,B)
        var colors = dark
            ? new Dictionary<string, Color>
            {
                ["BgBrush"] = Color.FromRgb(0x1E, 0x1E, 0x2E),
                ["SurfaceBrush"] = Color.FromRgb(0x31, 0x32, 0x44),
                ["SurfaceAltBrush"] = Color.FromRgb(0x45, 0x47, 0x5A),
                ["TextBrush"] = Color.FromRgb(0xCD, 0xD6, 0xF4),
                ["MutedBrush"] = Color.FromRgb(0xA6, 0xAD, 0xC8),
                ["AccentBrush"] = Color.FromRgb(0x89, 0xB4, 0xFA),
                ["DangerBrush"] = Color.FromRgb(0xF3, 0x8B, 0xA8),
                ["SuccessBrush"] = Color.FromRgb(0xA6, 0xE3, 0xA1),
                ["WarningBrush"] = Color.FromRgb(0xF9, 0xE2, 0xAF),
                ["ButtonTextBrush"] = Color.FromRgb(0x1E, 0x1E, 0x2E),
                ["BorderBrush"] = Color.FromRgb(0x45, 0x47, 0x5A),
                ["OverlayBrush"] = Color.FromArgb(0xCC, 0x1E, 0x1E, 0x2E),
                ["ScrollThumbBrush"] = Color.FromRgb(0x58, 0x5B, 0x70),
                ["ScrollTrackBrush"] = Color.FromRgb(0x31, 0x32, 0x44),
            }
            : new Dictionary<string, Color>
            {
                ["BgBrush"] = Color.FromRgb(0xEF, 0xF1, 0xF5),
                ["SurfaceBrush"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
                ["SurfaceAltBrush"] = Color.FromRgb(0xCC, 0xD0, 0xDA),
                ["TextBrush"] = Color.FromRgb(0x4C, 0x4F, 0x69),
                ["MutedBrush"] = Color.FromRgb(0x6C, 0x6F, 0x85),
                ["AccentBrush"] = Color.FromRgb(0x1E, 0x66, 0xF5),
                ["DangerBrush"] = Color.FromRgb(0xD2, 0x0F, 0x39),
                ["SuccessBrush"] = Color.FromRgb(0x40, 0xA0, 0x2B),
                ["WarningBrush"] = Color.FromRgb(0xDF, 0x8E, 0x1D),
                ["ButtonTextBrush"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
                ["BorderBrush"] = Color.FromRgb(0xCC, 0xD0, 0xDA),
                ["OverlayBrush"] = Color.FromArgb(0xAA, 0xEF, 0xF1, 0xF5),
                ["ScrollThumbBrush"] = Color.FromRgb(0x9C, 0xA0, 0xB0),
                ["ScrollTrackBrush"] = Color.FromRgb(0xE6, 0xE9, 0xEF),
            };

        foreach (var (key, color) in colors)
        {
            // Freeze brushes for performance; create new instance each time so DynamicResource updates
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            app.Resources[key] = brush;
        }
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
}
