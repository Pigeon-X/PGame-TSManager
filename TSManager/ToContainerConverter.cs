using System;
using System.Globalization;
using System.Windows.Data;

namespace TSManager;

[ValueConversion(typeof(object), typeof(ServerContainer))]
public class ToContainerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }
}