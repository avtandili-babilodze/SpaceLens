using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SpaceLens.Infrastructure.Converters;

/// <summary>Visible when the value's text equals the parameter (or one of several separated by '|').</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var options = (parameter as string ?? "").Split('|');
        return options.Contains(value?.ToString(), StringComparer.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
