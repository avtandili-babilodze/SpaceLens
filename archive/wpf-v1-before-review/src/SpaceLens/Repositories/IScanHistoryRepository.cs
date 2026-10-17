using SpaceLens.Models;

namespace SpaceLens.Repositories;

public interface IScanHistoryRepository
{
    Task InitializeAsync();
    IScanWriter CreateScan(string root);
    Task<IReadOnlyList<StorageSnapshot>> GetSnapshotsAsync();
    Task<StorageSnapshot?> GetSnapshotAsync(long id);
    Task<FileEntry?> GetEntryAsync(long id);
    Task<FileEntry?> GetRootAsync(long snapshotId);
    Task<FileEntry?> FindPathAsync(long snapshotId, string path);
    Task<EntryPage> GetChildrenAsync(EntryQuery query);
    Task<FolderDetails> GetDetailsAsync(FileEntry entry);
    Task<IReadOnlyList<FileEntry>> GetCandidatesAsync(long snapshotId, int limit = 500);
    Task<SnapshotComparison> CompareAsync(long beforeId, long afterId);
    Task<IReadOnlyList<IReadOnlyList<FileEntry>>> GetDuplicateCandidatesAsync(long snapshotId);
}

public interface IScanWriter : IDisposable
{
    long SnapshotId { get; }
    long Insert(FileEntry entry);
    void CompleteFolder(long id, long size, long files, long folders, string? warning);
    void Finish(ScanStatus status, ScanProgress progress);
}
