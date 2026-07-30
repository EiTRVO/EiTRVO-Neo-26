using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EiTRVO.UI.Converters;

/// <summary>
/// Converts a double ratio (e.g. 0.5) to a star-sized <see cref="GridLength"/>.
/// Used by the memory slider color-zone indicator bar.
/// </summary>
public class RatioToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double ratio = value is double d ? d : 0;
        return new GridLength(Math.Max(ratio, 0), GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
