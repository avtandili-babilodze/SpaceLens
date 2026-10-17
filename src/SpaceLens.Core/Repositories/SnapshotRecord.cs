namespace SpaceLens.Core.Repositories;

/// <summary>Everything persisted for one completed scan.</summary>
public sealed record SnapshotRecord(
    string RootPath,
    DateTime CreatedUtc,
    long TotalBytes,
    long FileCount,
    long FolderCount,
    long UnreadableFolderCount,
    IReadOnlyList<SnapshotFolderRow> Folders,
    IReadOnlyList<SnapshotFileRow> LargeFiles);
