using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PejPass.Wpf.Converters;

/// <summary>
/// Visible when value is a non-empty string; Collapsed otherwise.
/// </summary>
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        var hasText = !string.IsNullOrWhiteSpace(text);
        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
