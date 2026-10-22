using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PejPass.Wpf.Converters;

/// <summary>
/// Visible when the string is null/empty (for search placeholders).
/// </summary>
public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        return string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
