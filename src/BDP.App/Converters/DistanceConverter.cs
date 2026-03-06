using System.Globalization;

namespace BDP.App.Converters;

public sealed class DistanceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double km)
            return $"{km:F2} km";
        if (value is float kmF)
            return $"{kmF:F2} km";
        return "0.00 km";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
