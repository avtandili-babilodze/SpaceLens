namespace SpaceLens.Core.Models;

public sealed class ScanResult
{
    public required FolderNode Root { get; init; }

    public required DateTime StartedUtc { get; init; }

    public required TimeSpan Duration { get; init; }

    /// <summary>False when the scan was cancelled; totals then cover only what was read.</summary>
    public required bool IsComplete { get; init; }

    /// <summary>Folders that could not be listed, usually because access was denied.</summary>
    public required long UnreadableFolderCount { get; init; }

    /// <summary>Junctions and symbolic links that were recorded but not followed.</summary>
    public required long SkippedLinkCount { get; init; }

    /// <summary>Drive capacity as reported by Windows when the scan finished.</summary>
    public DriveSummary? Drive { get; init; }

    public string RootPath => Root.FullPath;

    public DateTime CompletedUtc => StartedUtc + Duration;

    public bool IsDriveRoot => string.Equals(Path.GetPathRoot(RootPath), RootPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Space Windows reports as used that the scan could not attribute to readable files:
    /// system and restricted folders, the page file, allocation overhead and so on.
    /// Only meaningful when a whole drive was scanned.
    /// </summary>
    public long UnaccountedBytes => IsDriveRoot && IsComplete && Drive is not null
        ? Math.Max(0, Drive.UsedBytes - Root.Size)
        : 0;
}
