using System.Globalization;
using System.Windows.Data;

namespace SpaceLens.Infrastructure.Converters;

/// <summary>True when the value's text equals the parameter; used for segmented toggles bound to enums.</summary>
public sealed class EqualsToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
