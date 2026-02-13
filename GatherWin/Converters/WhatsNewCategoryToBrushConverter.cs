using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GatherWin.Converters;

/// <summary>Maps What's New category strings to background/border colors.</summary>
public class WhatsNewCategoryToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var category = value as string ?? "";
        var isBorder = parameter as string == "border";

        return category switch
        {
            "New Agent"             => isBorder ? (object)new SolidColorBrush(Color.FromRgb(100, 181, 246)) // #64B5F6
                                                : new SolidColorBrush(Color.FromRgb(227, 242, 253)),         // #E3F2FD
            "New Skill"             => isBorder ? (object)new SolidColorBrush(Color.FromRgb(129, 199, 132)) // #81C784
                                                : new SolidColorBrush(Color.FromRgb(232, 245, 233)),         // #E8F5E9
            "Platform Announcement" => isBorder ? (object)new SolidColorBrush(Color.FromRgb(255, 183, 77))  // #FFB74D
                                                : new SolidColorBrush(Color.FromRgb(255, 243, 224)),         // #FFF3E0
            "Trending Post"         => isBorder ? (object)new SolidColorBrush(Color.FromRgb(206, 147, 216)) // #CE93D8
                                                : new SolidColorBrush(Color.FromRgb(243, 229, 245)),         // #F3E5F5
            "Fee Schedule Changed"  => isBorder ? (object)new SolidColorBrush(Color.FromRgb(229, 115, 115)) // #E57373
                                                : new SolidColorBrush(Color.FromRgb(255, 235, 238)),         // #FFEBEE
            "Fee Schedule"          => isBorder ? (object)new SolidColorBrush(Color.FromRgb(176, 190, 197)) // #B0BEC5
                                                : new SolidColorBrush(Color.FromRgb(236, 239, 241)),         // #ECEFF1
            "API Spec Changed"      => isBorder ? (object)new SolidColorBrush(Color.FromRgb(229, 115, 115)) // #E57373
                                                : new SolidColorBrush(Color.FromRgb(255, 235, 238)),         // #FFEBEE
            "API Spec"              => isBorder ? (object)new SolidColorBrush(Color.FromRgb(176, 190, 197)) // #B0BEC5
                                                : new SolidColorBrush(Color.FromRgb(236, 239, 241)),         // #ECEFF1
            _                       => isBorder ? (object)new SolidColorBrush(Colors.LightGray)
                                                : new SolidColorBrush(Colors.White)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
