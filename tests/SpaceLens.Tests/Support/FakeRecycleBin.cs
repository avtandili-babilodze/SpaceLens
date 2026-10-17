using SpaceLens.Core.Services.Cleanup;

namespace SpaceLens.Tests.Support;

/// <summary>Records what would have been recycled instead of touching the disk.</summary>
public sealed class FakeRecycleBin(long bytes = 0) : IRecycleBinService
{
    public List<string> Recycled { get; } = [];

    public RecycleBinInfo Query(string driveRoot) => new(bytes, bytes > 0 ? 3 : 0);

    public RecycleOutcome MoveToRecycleBin(IReadOnlyList<string> paths)
    {
        Recycled.AddRange(paths);
        return new RecycleOutcome(paths, [], WasCancelled: false);
    }

    public void Empty(string driveRoot)
    {
    }
}
