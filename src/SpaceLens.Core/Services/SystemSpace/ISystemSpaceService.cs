using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.SystemSpace;

public interface ISystemSpaceService
{
    /// <summary>
    /// Measures what Windows keeps on the system drive. Folders already in <paramref name="scan"/> are taken from it;
    /// anything else is measured directly, skipping what Windows won't let a normal user read.
    /// </summary>
    Task<IReadOnlyList<SystemSpaceItem>> MeasureAsync(ScanResult? scan, CancellationToken cancellationToken);
}
