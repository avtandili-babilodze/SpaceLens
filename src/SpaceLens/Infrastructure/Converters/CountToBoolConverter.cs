using System.Globalization;
using System.Windows.Data;

namespace SpaceLens.Infrastructure.Converters;

/// <summary>True for a positive count.</summary>
public sealed class CountToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int count && count > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
