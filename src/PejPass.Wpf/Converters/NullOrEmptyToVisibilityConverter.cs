using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PejPass.Wpf.Converters;

/// <summary>
/// Visible when value is a non-empty string or a non-empty collection;
/// Collapsed otherwise.
/// </summary>
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
            return Visibility.Collapsed;

        if (value is string text)
            return string.IsNullOrWhiteSpace(text)
                ? Visibility.Collapsed
                : Visibility.Visible;

        if (value is ICollection collection)
            return collection.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (value is IEnumerable enumerable)
        {
            var enumerator = enumerable.GetEnumerator();
            try
            {
                return enumerator.MoveNext()
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
