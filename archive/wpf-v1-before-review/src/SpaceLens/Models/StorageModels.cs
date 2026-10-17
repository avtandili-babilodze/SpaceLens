namespace SpaceLens.Models;

public sealed record StorageSnapshot(long Id, string RootPath, DateTime CreatedUtc, ScanStatus Status, long Size, long Files, long Folders, long Errors, long Skipped)
{
    public string DisplayName => $"{CreatedUtc.ToLocalTime():MMM d, HH:mm:ss} · {ByteSize.Format(Size)} · {RootPath}";
    public string SizeText => ByteSize.Format(Size);
    public string StatusText => Status == ScanStatus.Completed && Errors > 0 ? $"Completed · {Errors:N0} access errors" : Status.ToString();
    public override string ToString() => DisplayName;
}
public sealed record ScanProgress(long Files, long Folders, long Bytes, long Errors, long Skipped, string CurrentPath);
public sealed record DriveEntry(string Path, string Name, long Total, long Available, string Format)
{
    public long Used => Total - Available;
    public double UsedPercent => Total > 0 ? 100d * Used / Total : 0;
    public string DisplayName => $"{Name} ({Path.TrimEnd('\\')})";
    public string Description => $"{ByteSize.Format(Total)} · {Format}";
    public override string ToString() => DisplayName;
}
public sealed record EntryPage(IReadOnlyList<FileEntry> Entries, long Total);
public sealed record EntryQuery(long SnapshotId, long ParentId, string Search = "", string Sort = "Size", bool Descending = true, string Filter = "All items", int Offset = 0, int Limit = 300, bool Recursive = false);
public sealed record CategoryUsage(string Name, long Size, long Count, double Percent, string Color)
{
    public string SizeText => ByteSize.Format(Size);
    public string PercentText => $"{Percent:0}%";
}
public sealed record FolderDetails(FileEntry Entry, IReadOnlyList<CategoryUsage> Categories, IReadOnlyList<FileEntry> LargestFiles, long ReviewBytes, long LargeFileCount);
public sealed record CleanupPlan(long GoalBytes, long PotentialBytes, IReadOnlyList<FileEntry> Candidates, string Explanation)
{
    public string PotentialText => ByteSize.Format(PotentialBytes);
    public bool MeetsGoal => PotentialBytes >= GoalBytes;
}
public sealed record StorageChange(string FullPath, bool IsDirectory, long Before, long After, string Kind)
{
    public long Delta => After - Before;
    public string Name => System.IO.Path.GetFileName(FullPath.TrimEnd('\\'));
    public string BeforeText => ByteSize.Format(Before);
    public string AfterText => ByteSize.Format(After);
    public string DeltaText => (Delta > 0 ? "+" : "") + ByteSize.Format(Delta);
}
public sealed record SnapshotComparison(StorageSnapshot Before, StorageSnapshot After, IReadOnlyList<StorageChange> Changes)
{
    public long Delta => After.Size - Before.Size;
    public string DeltaText => (Delta > 0 ? "+" : "") + ByteSize.Format(Delta);
}
public sealed record DuplicateGroup(string Hash, IReadOnlyList<FileEntry> Files)
{
    public long RedundantBytes => Files.Count > 1 ? Files[0].Size * (Files.Count - 1) : 0;
    public string Summary => $"{Files.Count} identical files · {ByteSize.Format(RedundantBytes)} redundant logical size";
}
public sealed record AiReference(long EntryId, string Label, string FullPath);
public sealed record ChatMessage(string Role, string Text, IReadOnlyList<AiReference>? References = null)
{
    public bool WasSharedWithCloud { get; init; }
    public string Speaker => Role == "user" ? "You" : Role == "error" ? "Connection issue" : Role == "local" ? "SpaceLens · offline" : "Claude Haiku 5.5";
}
public sealed record AiContext(string Json, IReadOnlyList<AiReference> References);
public sealed record AiReply(string Text, IReadOnlyList<AiReference> References);
