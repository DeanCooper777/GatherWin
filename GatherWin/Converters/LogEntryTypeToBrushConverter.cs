using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GatherWin.Models;

namespace GatherWin.Converters;

public class LogEntryTypeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LogEntryType type)
        {
            return type switch
            {
                LogEntryType.Comment  => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),  // green
                LogEntryType.Inbox    => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),  // amber/yellow
                LogEntryType.FeedPost => new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)),  // cyan
                LogEntryType.Channel  => new SolidColorBrush(Color.FromRgb(0xAB, 0x47, 0xBC)),  // purple
                LogEntryType.Error    => new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),  // red
                _ => new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99))                       // dim gray
            };
        }
        return new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
