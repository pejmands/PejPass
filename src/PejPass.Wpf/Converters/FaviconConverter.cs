using System.Globalization;
using System.Windows.Data;
using PejPass.Domain.Entities;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Converters;

/// <summary>
/// VaultEntry → ImageSource (favicon or letter avatar).
/// </summary>
public sealed class FaviconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is VaultEntry entry)
            return FaviconService.GetImage(entry.Url, entry.Title);

        return FaviconService.CreateLetterAvatar("?");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
