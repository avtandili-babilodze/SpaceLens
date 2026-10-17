using System.Globalization;
using System.Windows.Data;
using SpaceLens.Core.Models;

namespace SpaceLens.Infrastructure.Converters;

/// <summary>Formats a byte count like File Explorer ("12.4 GB").</summary>
public sealed class BytesToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? ByteSize.Format(bytes) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
