using SpaceLens.Models;
using SpaceLens.Repositories;

namespace SpaceLens.Services.Storage;

public interface IStorageHistoryService
{
    Task<IReadOnlyList<StorageSnapshot>> GetHistoryAsync();
    Task<SnapshotComparison> CompareAsync(long before, long after);
}
public sealed class StorageHistoryService(IScanHistoryRepository repository) : IStorageHistoryService
{
    public Task<IReadOnlyList<StorageSnapshot>> GetHistoryAsync() => repository.GetSnapshotsAsync();
    public Task<SnapshotComparison> CompareAsync(long before, long after) => repository.CompareAsync(before, after);
}
