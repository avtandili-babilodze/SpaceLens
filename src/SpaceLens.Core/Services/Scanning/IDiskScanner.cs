using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Scanning;

public interface IDiskScanner
{
    /// <summary>
    /// Scans a drive or folder and builds an in-memory tree with recursive sizes.
    /// Cancelling returns a partial result (<see cref="ScanResult.IsComplete"/> is false) instead of throwing.
    /// </summary>
    Task<ScanResult> ScanAsync(string path, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}
