namespace SpaceLens.Core.Repositories;

/// <summary>A folder of 1 MB or more as recorded in history.</summary>
public sealed record SnapshotFolderRow(string Path, long Bytes, long FileCount);
