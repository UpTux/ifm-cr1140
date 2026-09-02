using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Cr1140.AvaloniaDemo.Views;

public class BoolToInOutConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? "IN" : "OUT";
        return "OUT";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? new SolidColorBrush(Color.Parse("#00cc66")) : new SolidColorBrush(Color.Parse("#ff4444"));
        return new SolidColorBrush(Color.Parse("#ff4444"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
