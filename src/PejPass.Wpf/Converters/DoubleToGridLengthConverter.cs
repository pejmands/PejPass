using System.Globalization;
using System.Windows.Data;

namespace PejPass.Wpf.Converters;

/// <summary>
/// Converts 0..1 progress + parent ActualWidth (via MultiBinding) is simpler;
/// this one converts remaining fraction * maxWidth passed as ConverterParameter,
/// or just returns a star-less fixed approach via code-behind-free binding to TotpProgressWidth.
/// Prefer binding TotpProgressWidth (pixels) from ViewModel instead.
/// </summary>
public sealed class PassThroughDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value ?? 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
