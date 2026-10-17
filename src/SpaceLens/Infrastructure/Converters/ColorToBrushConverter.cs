using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SpaceLens.Infrastructure.Converters;

/// <summary>Converts "#RRGGBB" strings from the view models into frozen brushes.</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = [];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || text.Length == 0)
        {
            return Brushes.Transparent;
        }

        if (!Cache.TryGetValue(text, out var brush))
        {
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));
            brush.Freeze();
            Cache[text] = brush;
        }

        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
