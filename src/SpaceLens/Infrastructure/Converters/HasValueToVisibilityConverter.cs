using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SpaceLens.Infrastructure.Converters;

/// <summary>Collapses for null, empty strings and empty collections. "Invert" reverses.</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            ICollection collection => collection.Count > 0,
            _ => true
        };
        return hasValue ^ BoolToVisibilityConverter.IsInverted(parameter) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
