using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Scanning;

/// <summary>Reads drive capacity from Windows. Network drives are left out because scanning them is slow.</summary>
public sealed class DriveService : IDriveService
{
    public IReadOnlyList<DriveSummary> GetDrives()
    {
        var drives = new List<DriveSummary>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is DriveType.Fixed or DriveType.Removable && TryRead(drive) is { } summary)
            {
                drives.Add(summary);
            }
        }

        return drives;
    }

    public DriveSummary? TryGetDrive(string path)
    {
        var root = Path.GetPathRoot(path);
        return string.IsNullOrEmpty(root) ? null : TryRead(new DriveInfo(root));
    }

    private static DriveSummary? TryRead(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
            {
                return null;
            }

            return new DriveSummary(
                drive.RootDirectory.FullName,
                drive.VolumeLabel,
                drive.DriveFormat,
                drive.DriveType,
                drive.TotalSize,
                drive.TotalFreeSpace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
