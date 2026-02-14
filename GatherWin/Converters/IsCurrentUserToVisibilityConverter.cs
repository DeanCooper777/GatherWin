using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GatherWin.Converters;

/// <summary>
/// Converts an author name to Visibility.Visible if it matches the current user,
/// Collapsed otherwise. Used to show edit/options buttons only on own messages.
/// </summary>
public class IsCurrentUserToVisibilityConverter : IValueConverter
{
    /// <summary>The current user's display name.</summary>
    public static string CurrentUserName { get; set; } = "OnTheEdgeOfReality";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string author && string.Equals(author, CurrentUserName, StringComparison.OrdinalIgnoreCase))
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
