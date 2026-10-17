using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SpaceLens.Infrastructure.Converters;

/// <summary>
/// Visible only when every bound boolean matches the parameter pattern, e.g. "False,False,True"
/// for "no scan, not scanning, on a workspace page".
/// </summary>
public sealed class BoolPatternToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var pattern = (parameter as string ?? "").Split(',', StringSplitOptions.TrimEntries);
        var matches = pattern.Length == values.Length && values.Zip(pattern).All(pair => Matches(pair.First, pair.Second));
        return matches ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(object value, string expected) =>
        value is bool actual && bool.TryParse(expected, out var wanted) && actual == wanted;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => [];
}
