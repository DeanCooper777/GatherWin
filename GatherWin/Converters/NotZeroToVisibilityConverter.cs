using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GatherWin.Converters;

/// <summary>Shows element when value is > 0 (for tab badge counts).</summary>
public class NotZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
