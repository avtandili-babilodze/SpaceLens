namespace SpaceLens.Core.Models;

/// <summary>A saved summary of a completed scan: totals, folders of 1 MB or more and files of 100 MB or more.</summary>
public sealed record StorageSnapshot(
    long Id,
    string RootPath,
    DateTime CreatedUtc,
    long TotalBytes,
    long FileCount,
    long FolderCount,
    long UnreadableFolderCount)
{
    public string DisplayName => $"{CreatedUtc.ToLocalTime():MMM d, yyyy  HH:mm}  ·  {ByteSize.Format(TotalBytes)}";

    public override string ToString() => DisplayName;
}
