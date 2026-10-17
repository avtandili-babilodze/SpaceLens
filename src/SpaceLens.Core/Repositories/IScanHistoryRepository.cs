using SpaceLens.Core.Models;

namespace SpaceLens.Core.Repositories;

public interface IScanHistoryRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<StorageSnapshot> SaveAsync(SnapshotRecord record, CancellationToken cancellationToken = default);

    /// <summary>All snapshots, newest first.</summary>
    Task<IReadOnlyList<StorageSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default);

    Task<StorageSnapshot?> GetSnapshotAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The newest snapshot of <paramref name="rootPath"/> taken before snapshot <paramref name="beforeId"/>.</summary>
    Task<StorageSnapshot?> GetPreviousAsync(string rootPath, long beforeId, CancellationToken cancellationToken = default);

    /// <summary>Every recorded folder whose size differs between the two snapshots, including folders present in only one.</summary>
    Task<IReadOnlyList<FolderChange>> GetFolderChangesAsync(long beforeId, long afterId, CancellationToken cancellationToken = default);

    /// <summary>Large files in the later snapshot that are new or changed size.</summary>
    Task<IReadOnlyList<LargeFileChange>> GetLargeFileChangesAsync(
        long beforeId,
        long afterId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes all but the newest <paramref name="keep"/> snapshots of a location.</summary>
    Task PruneAsync(string rootPath, int keep, CancellationToken cancellationToken = default);

    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}
