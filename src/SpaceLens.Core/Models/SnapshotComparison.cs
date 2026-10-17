namespace SpaceLens.Core.Models;

/// <summary>What changed between two snapshots of the same location.</summary>
public sealed record SnapshotComparison(
    StorageSnapshot Before,
    StorageSnapshot After,
    IReadOnlyList<FolderChange> Grew,
    IReadOnlyList<FolderChange> Shrank,
    IReadOnlyList<LargeFileChange> NewLargeFiles)
{
    public long NetChange => After.TotalBytes - Before.TotalBytes;

    public TimeSpan Elapsed => After.CreatedUtc - Before.CreatedUtc;
}
