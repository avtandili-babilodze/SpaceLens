using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SpaceLens.Infrastructure.Converters;

/// <summary>Indents a tree row by its nesting level, so the row itself can span the full width.</summary>
public sealed class TreeIndentConverter : IValueConverter
{
    private const double IndentPerLevel = 18;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = 0;
        for (var item = value as TreeViewItem; item is not null; item = ItemsControl.ItemsControlFromItemContainer(item) as TreeViewItem)
        {
            level++;
        }

        return new Thickness(Math.Max(0, level - 1) * IndentPerLevel, 0, 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
