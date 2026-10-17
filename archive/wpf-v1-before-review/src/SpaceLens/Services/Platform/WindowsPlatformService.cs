using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using SpaceLens.Models;

namespace SpaceLens.Services.Platform;

public interface IWindowsPlatformService
{
    Task<IReadOnlyList<DriveEntry>> GetDrivesAsync();
    string? PickFolder();
    void Reveal(string path);
    void CopyPath(string path);
}

public sealed class WindowsPlatformService : IWindowsPlatformService
{
    public Task<IReadOnlyList<DriveEntry>> GetDrivesAsync() => Task.Run<IReadOnlyList<DriveEntry>>(() =>
    {
        var drives = new List<DriveEntry>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType == DriveType.Network) continue;
                drives.Add(new(drive.RootDirectory.FullName, string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local disk" : drive.VolumeLabel, drive.TotalSize, drive.AvailableFreeSpace, drive.DriveFormat));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return drives;
    });
    public string? PickFolder()
    {
        var picker = new OpenFolderDialog { Title = "Choose a folder to scan", Multiselect = false };
        return picker.ShowDialog() == true ? picker.FolderName : null;
    }
    public void Reveal(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full) && !File.Exists(full)) throw new FileNotFoundException("This item no longer exists on disk.");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
        // ArgumentList prevents a filename from being interpreted as shell syntax.
        start.ArgumentList.Add("/select," + full);
        Process.Start(start);
    }
    public void CopyPath(string path) => System.Windows.Clipboard.SetText(path);
}
