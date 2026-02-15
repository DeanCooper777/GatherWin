using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace GatherWin.Converters;

/// <summary>Shared limit used by character count converters. Updated from Options.</summary>
public static class CharLimitSettings
{
    public static int MaxLength { get; set; } = 2000;
    public static int ChannelMaxLength { get; set; } = 5000;
}

/// <summary>Converts a string to a "X/10,000" character count display.
/// Pass an integer ConverterParameter to override the limit (e.g., for channels).</summary>
public class CharCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? "";
        var max = parameter is string s && int.TryParse(s, out var p) ? p : CharLimitSettings.MaxLength;
        return $"{text.Length:N0}/{max:N0}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}

/// <summary>Returns red brush when text exceeds the limit, otherwise gray.
/// Pass an integer ConverterParameter to override the limit (e.g., for channels).</summary>
public class CharCountBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? "";
        var max = parameter is string s && int.TryParse(s, out var p) ? p : CharLimitSettings.MaxLength;
        return text.Length > max
            ? new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C))
            : new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
