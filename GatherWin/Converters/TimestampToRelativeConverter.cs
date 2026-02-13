using System.Globalization;
using System.Windows.Data;

namespace GatherWin.Converters;

/// <summary>Converts DateTimeOffset to a relative time string like "2 min ago".</summary>
public class TimestampToRelativeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTimeOffset dto)
        {
            var elapsed = DateTimeOffset.Now - dto;

            if (elapsed.TotalSeconds < 60)
                return "just now";
            if (elapsed.TotalMinutes < 60)
                return $"{(int)elapsed.TotalMinutes} min ago";
            if (elapsed.TotalHours < 24)
                return $"{(int)elapsed.TotalHours}h ago";
            if (elapsed.TotalDays < 7)
                return $"{(int)elapsed.TotalDays}d ago";

            return dto.LocalDateTime.ToString("MMM d, yyyy");
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
