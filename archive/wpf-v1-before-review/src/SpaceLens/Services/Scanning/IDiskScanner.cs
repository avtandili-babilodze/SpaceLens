using SpaceLens.Models;

namespace SpaceLens.Services.Scanning;

public interface IDiskScanner
{
    Task<StorageSnapshot> ScanAsync(string path, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}
