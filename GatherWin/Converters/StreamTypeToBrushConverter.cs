using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GatherWin.Models;

namespace GatherWin.Converters;

/// <summary>Maps ActivityType to a background Brush for activity cards.</summary>
public class StreamTypeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ActivityType type)
        {
            return type switch
            {
                ActivityType.Comment  => new SolidColorBrush(Color.FromRgb(232, 245, 233)),  // #E8F5E9
                ActivityType.Inbox    => new SolidColorBrush(Color.FromRgb(255, 248, 225)),  // #FFF8E1
                ActivityType.FeedPost => new SolidColorBrush(Color.FromRgb(224, 247, 250)),  // #E0F7FA
                ActivityType.Channel  => new SolidColorBrush(Color.FromRgb(243, 229, 245)),  // #F3E5F5
                _ => new SolidColorBrush(Colors.White)
            };
        }
        return new SolidColorBrush(Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
