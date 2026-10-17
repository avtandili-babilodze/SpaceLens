using System.Globalization;

namespace SpaceLens.Core.Models;

/// <summary>
/// Formats byte counts the way Windows Explorer does: binary multiples (1 GB = 1024^3 bytes) with short unit names.
/// </summary>
public static class ByteSize
{
    public const long KB = 1024;
    public const long MB = KB * 1024;
    public const long GB = MB * 1024;
    public const long TB = GB * 1024;

    private static readonly string[] Units = ["KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes) => Format(bytes, CultureInfo.CurrentCulture);

    public static string Format(long bytes, IFormatProvider provider)
    {
        if (bytes < 0)
        {
            return "-" + Format(bytes == long.MinValue ? long.MaxValue : -bytes, provider);
        }

        if (bytes < KB)
        {
            return bytes == 1 ? "1 byte" : string.Create(provider, $"{bytes} bytes");
        }

        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // Three significant digits at most, like Explorer: 5.4 GB, 12.4 GB, 380 GB.
        var format = value < 100 ? "0.#" : "0";
        return value.ToString(format, provider) + " " + Units[unit];
    }

    /// <summary>Formats a change in size with an explicit sign, e.g. "+12 GB".</summary>
    public static string FormatDelta(long bytes)
    {
        if (bytes == 0)
        {
            return "No change";
        }

        return bytes > 0 ? "+" + Format(bytes) : "−" + Format(-bytes);
    }

    public static double ToGigabytes(long bytes) => (double)bytes / GB;
}
