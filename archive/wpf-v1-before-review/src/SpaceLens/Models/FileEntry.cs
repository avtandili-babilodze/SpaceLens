using System.Globalization;

namespace SpaceLens.Models;

public enum CleanupRisk { LowRisk, ReviewRequired, HighRisk, Protected }
public enum ScanStatus { Scanning, Completed, Cancelled, Interrupted, Failed }

public sealed record FileEntry
{
    public long Id { get; init; }
    public long SnapshotId { get; init; }
    public long? ParentId { get; init; }
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsReparsePoint { get; init; }
    public long Size { get; init; }
    public long FileCount { get; init; }
    public long FolderCount { get; init; }
    public DateTime? ModifiedUtc { get; init; }
    public string Extension { get; init; } = "";
    public string Category { get; init; } = "Other";
    public CleanupRisk Risk { get; init; } = CleanupRisk.ReviewRequired;
    public string Reason { get; init; } = "";
    public string? Warning { get; init; }
    public string SizeText => ByteSize.Format(Size);
    public string TypeText => IsReparsePoint ? "Link · skipped" : IsDirectory ? "Folder" : string.IsNullOrEmpty(Extension) ? "File" : Extension.TrimStart('.').ToUpperInvariant() + " file";
    public string RiskText => Risk switch { CleanupRisk.LowRisk => "Low Risk", CleanupRisk.HighRisk => "High Risk", CleanupRisk.Protected => "Protected", _ => "Review Required" };
    public string ModifiedText => ModifiedUtc?.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture) ?? "Unavailable";
    public string Icon => IsDirectory ? "\uE8B7" : Category switch { "Video" => "\uE714", "Images" => "\uEB9F", "Audio" => "\uE8D6", "Archives" => "\uF012", "Development" => "\uE943", "Applications" => "\uE7C3", _ => "\uE8A5" };
    public string IconColor => IsDirectory ? "#E8BA67" : Category switch { "Video" => "#B394E9", "Images" => "#6FA9E5", "Development" => "#71BCA7", _ => "#9DAFC8" };
}

public static class ByteSize
{
    public static string Format(long bytes)
    {
        double value = Math.Abs((double)bytes);
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return (bytes < 0 ? "−" : "") + value.ToString(unit == 0 ? "N0" : "0.#", CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
