using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Scanning;

public interface IDriveService
{
    /// <summary>Ready local and removable drives, in drive-letter order.</summary>
    IReadOnlyList<DriveSummary> GetDrives();

    /// <summary>The drive that contains <paramref name="path"/>, or null when it isn't available.</summary>
    DriveSummary? TryGetDrive(string path);
}
