using System.Globalization;
using System.Text.RegularExpressions;
using SpaceLens.Models;
using SpaceLens.Repositories;

namespace SpaceLens.Services.Cleanup;

public interface ICleanupPlanner
{
    Task<CleanupPlan> CreatePlanAsync(StorageSnapshot snapshot, long goalBytes, CancellationToken cancellationToken = default);
}

public sealed class CleanupPlanner(IScanHistoryRepository repository) : ICleanupPlanner
{
    public async Task<CleanupPlan> CreatePlanAsync(StorageSnapshot snapshot, long goalBytes, CancellationToken cancellationToken = default)
    {
        if (snapshot.Status != ScanStatus.Completed) throw new InvalidOperationException("Finish a scan before creating a cleanup plan. Cancelled and interrupted results are incomplete.");
        if (goalBytes <= 0) throw new ArgumentOutOfRangeException(nameof(goalBytes), "Enter a positive storage goal.");
        var available = await repository.GetCandidatesAsync(snapshot.Id, 5000);
        var chosen = new List<FileEntry>(); long potential = 0;
        foreach (var file in available)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.IsDirectory || file.IsReparsePoint || file.Risk is CleanupRisk.HighRisk or CleanupRisk.Protected) continue;
            chosen.Add(file); potential += file.Size;
            if (potential >= goalBytes || chosen.Count >= 200) break;
        }
        string explanation = potential >= goalBytes
            ? "This review list reaches your target only if every selected file is no longer needed. Inspect each file and its backup before taking any action."
            : "The reviewed candidate pool does not reach your target. No system or high-risk files have been added to fill the gap.";
        explanation += " Potential savings are logical file bytes, not guaranteed physical space. No files are automatically classified as safe from metadata alone. This app does not delete files.";
        if (snapshot.Errors > 0) explanation += " Inaccessible content was excluded from the scan.";
        return new(goalBytes, potential, chosen, explanation);
    }
}

public static partial class StorageGoalParser
{
    [GeneratedRegex(@"(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>TiB|GiB|MiB|TB|GB|MB)?", RegexOptions.IgnoreCase)]
    private static partial Regex Amount();
    public static long Parse(string input)
    {
        if (Regex.IsMatch(input, @"-\s*\d")) throw new FormatException("Enter a positive storage goal.");
        var match = Amount().Match(input);
        if (!match.Success || !double.TryParse(match.Groups["value"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
            throw new FormatException("Enter a goal such as 25 GiB or 'I need to free 25 GB'.");
        double factor = match.Groups["unit"].Value.ToUpperInvariant() switch { "MB" => 1_000_000d, "MIB" => 1048576d, "GB" => 1_000_000_000d, "TB" => 1_000_000_000_000d, "TIB" => 1099511627776d, _ => 1073741824d };
        double bytes = value * factor;
        if (bytes > long.MaxValue) throw new FormatException("The storage goal is too large.");
        return (long)Math.Ceiling(bytes);
    }
}
