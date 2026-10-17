namespace SpaceLens.Core.Models;

/// <summary>A progress report from a running scan.</summary>
public readonly record struct ScanProgress(long Files, long Folders, long Bytes, long UnreadableFolders, string CurrentPath);
