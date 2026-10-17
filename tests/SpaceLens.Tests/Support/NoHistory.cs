using SpaceLens.Core.Models;
using SpaceLens.Core.Repositories;
using SpaceLens.Core.Services.Storage;

namespace SpaceLens.Tests.Support;

/// <summary>A history that stores nothing, for tests that aren't about history.</summary>
public sealed class NoHistory : IStorageHistoryService
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public SnapshotRecord CreateRecord(ScanResult scan) =>
        new(scan.RootPath, scan.CompletedUtc, scan.Root.Size, scan.Root.FileCount, scan.Root.FolderCount, 0, [], []);

    public Task<SnapshotComparison?> SaveAsync(SnapshotRecord record, CancellationToken cancellationToken = default) =>
        Task.FromResult<SnapshotComparison?>(null);

    public Task<IReadOnlyList<StorageSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StorageSnapshot>>([]);

    public Task<SnapshotComparison> CompareAsync(long beforeId, long afterId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No history in tests.");

    public Task DeleteAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
