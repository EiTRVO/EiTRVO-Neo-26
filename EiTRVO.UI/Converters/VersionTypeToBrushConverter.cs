using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace EiTRVO.UI.Converters;

public class VersionTypeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string type = value as string ?? "";
        string hex = type switch
        {
            "release" => "#A6E3A1",
            "snapshot" => "#F9E2AF",
            "old_beta" => "#F38BA8",
            "old_alpha" => "#F38BA8",
            _ => "#89B4FA"
        };
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
