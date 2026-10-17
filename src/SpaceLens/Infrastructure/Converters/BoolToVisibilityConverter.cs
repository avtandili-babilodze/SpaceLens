using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SpaceLens.Infrastructure.Converters;

/// <summary>true → Visible. Pass "Invert" as the parameter to reverse.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is true;
        return visible ^ IsInverted(parameter) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ IsInverted(parameter);

    internal static bool IsInverted(object? parameter) => string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
}
