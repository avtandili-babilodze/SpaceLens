using System.Globalization;
using System.Text.RegularExpressions;
using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>
/// Reads a space goal from free text such as "30 GB", "free 1.5tb" or "I need 500 MB".
/// Units are binary, matching the sizes Windows Explorer shows.
/// </summary>
public static partial class StorageGoalParser
{
    private const long MaxGoalBytes = 1024 * ByteSize.TB;

    public static bool TryParse(string? text, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = AmountPattern().Match(text);
        if (!match.Success)
        {
            return false;
        }

        var number = match.Groups["value"].Value.Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return false;
        }

        var unit = match.Groups["unit"].Value.ToUpperInvariant();
        var multiplier = unit switch
        {
            "TB" or "TIB" or "T" => ByteSize.TB,
            "MB" or "MIB" or "M" => ByteSize.MB,
            _ => ByteSize.GB
        };

        var result = value * multiplier;
        if (result is < 1 or > MaxGoalBytes)
        {
            return false;
        }

        bytes = (long)Math.Ceiling(result);
        return true;
    }

    /// <summary>A number with an optional size unit; a bare number means gigabytes.</summary>
    [GeneratedRegex(@"(?<![\w.,-])(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>tib|tb|t|gib|gb|g|mib|mb|m)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex AmountPattern();
}
