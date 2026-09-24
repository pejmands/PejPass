using PejPass.Domain.Entities;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.Globalization;
using System.Windows.Data;

namespace PejPass.Wpf.Converters;

/// <summary>
/// VaultEntry or TrashRow → ImageSource (favicon or letter avatar).
/// </summary>
public sealed class FaviconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            VaultEntry entry => FaviconService.GetImage(entry.Url, entry.Title),
            TrashRow row => FaviconService.GetImage(row.Url, row.Title),
            _ => FaviconService.GetImage(null, "?")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
