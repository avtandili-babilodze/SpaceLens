namespace SpaceLens.Core.Services.Alerts;

public interface IDriveCheck
{
    /// <summary>
    /// Looks for drives below the free-space threshold, then scans the system drive and compares it with its previous
    /// scan. Saves that scan to history. Returns nothing when all is well.
    /// </summary>
    Task<IReadOnlyList<DriveAlert>> RunAsync(CancellationToken cancellationToken);
}
