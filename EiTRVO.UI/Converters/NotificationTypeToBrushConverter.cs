using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.UI.Converters;

public class NotificationTypeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var type = value is NotificationType nt ? nt : NotificationType.Info;
        string hex = type switch
        {
            NotificationType.Info => "#89B4FA",
            NotificationType.Success => "#A6E3A1",
            NotificationType.Warning => "#FAB387",
            NotificationType.Error => "#F38BA8",
            _ => "#89B4FA"
        };
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
