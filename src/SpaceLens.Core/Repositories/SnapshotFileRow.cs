namespace SpaceLens.Core.Repositories;

/// <summary>A file of 100 MB or more as recorded in history.</summary>
public sealed record SnapshotFileRow(string Path, long Bytes, DateTime LastWriteUtc);
