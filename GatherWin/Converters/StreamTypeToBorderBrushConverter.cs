using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GatherWin.Models;

namespace GatherWin.Converters;

/// <summary>Maps ActivityType to a border Brush for activity cards.</summary>
public class StreamTypeToBorderBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ActivityType type)
        {
            return type switch
            {
                ActivityType.Comment  => new SolidColorBrush(Color.FromRgb(165, 214, 167)),  // #A5D6A7
                ActivityType.Inbox    => new SolidColorBrush(Color.FromRgb(255, 224, 130)),  // #FFE082
                ActivityType.FeedPost => new SolidColorBrush(Color.FromRgb(128, 222, 234)),  // #80DEEA
                ActivityType.Channel  => new SolidColorBrush(Color.FromRgb(206, 147, 216)),  // #CE93D8
                _ => new SolidColorBrush(Colors.LightGray)
            };
        }
        return new SolidColorBrush(Colors.LightGray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
