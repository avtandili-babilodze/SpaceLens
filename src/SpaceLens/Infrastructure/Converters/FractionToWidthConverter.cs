using System.Globalization;
using System.Windows.Data;

namespace SpaceLens.Infrastructure.Converters;

/// <summary>Multiplies a fraction (0–1) by an available width, for proportional bars.</summary>
public sealed class FractionToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double fraction || values[1] is not double width || double.IsNaN(width))
        {
            return 0d;
        }

        // Keep tiny slices visible as a sliver instead of disappearing.
        return fraction <= 0 ? 0d : Math.Max(2, Math.Floor(fraction * width));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => [];
}
