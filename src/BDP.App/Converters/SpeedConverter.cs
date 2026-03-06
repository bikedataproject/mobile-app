using System.Globalization;

namespace BDP.App.Converters;

public sealed class SpeedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double speed)
            return $"{speed:F1} km/h";
        return "0.0 km/h";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
