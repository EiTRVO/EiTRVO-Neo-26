using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EiTRVO.UI.Converters;

public class BoolInvertConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool result = value is bool b ? !b : true;
        if (targetType == typeof(Visibility))
            return result ? Visibility.Visible : Visibility.Collapsed;
        return result;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
