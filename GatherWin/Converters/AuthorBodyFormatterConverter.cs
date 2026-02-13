using System.Globalization;
using System.Windows.Data;

namespace GatherWin.Converters;

/// <summary>
/// Formats activity body text with author attribution: [AuthorName] body text
/// If author is null or empty, returns just the body text.
/// </summary>
public class AuthorBodyFormatterConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var author = values[0] as string;
        var body = values[1] as string ?? "(empty)";

        if (string.IsNullOrEmpty(author))
            return body;

        return $"[{author}] {body}";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
