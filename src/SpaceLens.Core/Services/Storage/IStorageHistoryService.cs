using SpaceLens.Core.Models;
using SpaceLens.Core.Repositories;

namespace SpaceLens.Core.Services.Storage;

public interface IStorageHistoryService
{
    /// <summary>Creates the history database, or resets it when it was written by an incompatible version.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Summarises a completed scan for history: totals, folders of 1 MB or more and files of 100 MB or more.
    /// Reads the tree, so call it where the tree can't change (see <c>IScanSession.ReadAsync</c>).
    /// </summary>
    SnapshotRecord CreateRecord(ScanResult scan);

    /// <summary>Saves a record and compares it with the previous scan of the same location (null if none).</summary>
    Task<SnapshotComparison?> SaveAsync(SnapshotRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StorageSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default);

    Task<SnapshotComparison> CompareAsync(long beforeId, long afterId, CancellationToken cancellationToken = default);

    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}
