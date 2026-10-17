using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Cleanup;

public interface ICleanupExecutor
{
    /// <summary>Confirms with the user, then moves the items to the Recycle Bin and updates the scan.</summary>
    Task<CleanupOutcome> RecycleAsync(IReadOnlyList<StorageNode> items);

    /// <summary>Confirms with the user, then permanently empties the Recycle Bin of the scanned drive.</summary>
    Task<bool> EmptyRecycleBinAsync(long knownBytes);
}
