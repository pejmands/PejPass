using PejPass.Domain.Settings;
using System.Globalization;

namespace PejPass.Wpf.Services;

/// <summary>
/// Single source of truth for AppFontSizeNN resources. Every "AppFontSize{N}" reference
/// in XAML or code must have N listed in <see cref="Steps"/>.
/// </summary>
internal static class FontScale
{
    /// <summary>The size that the default AppFontSize resource is based on.</summary>
    public const double BaseSize = 13d;

    public static readonly IReadOnlyList<double> Steps =
        [10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 26, 28, 30];

    public static double ScaleFor(FontSizeMode mode) => mode switch
    {
        FontSizeMode.Small => 12d / 13d,
        FontSizeMode.Large => 15d / 13d,
        _ => 1d
    };

    public static string ResourceKey(double baseSize) =>
        string.Create(CultureInfo.InvariantCulture, $"AppFontSize{baseSize:0}");
}
