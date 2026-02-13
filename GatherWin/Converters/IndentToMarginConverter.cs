using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GatherWin.Converters;

/// <summary>Converts an indent level (int) to a left Margin Thickness for threaded discussions.</summary>
public class IndentToMarginConverter : IValueConverter
{
    private const double IndentPixels = 24.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int level)
            return new Thickness(level * IndentPixels, 0, 0, 3);
        return new Thickness(0, 0, 0, 3);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
