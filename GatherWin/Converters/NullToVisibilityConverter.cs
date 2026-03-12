using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GatherWin.Converters;

/// <summary>Returns Visible when the value IS null/empty, Collapsed when it has a value.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return Visibility.Visible;
        if (value is string s && string.IsNullOrEmpty(s)) return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
