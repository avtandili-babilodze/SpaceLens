using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.SystemSpace;

/// <summary>Measures Windows' own space users on the system drive. Read-only; changing any of it is left to Windows tools.</summary>
public sealed class SystemSpaceService : ISystemSpaceService
{
    private static readonly EnumerationOptions Everything = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public Task<IReadOnlyList<SystemSpaceItem>> MeasureAsync(ScanResult? scan, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<SystemSpaceItem>>(() => Measure(scan, cancellationToken), cancellationToken);

    private static List<SystemSpaceItem> Measure(ScanResult? scan, CancellationToken cancellationToken)
    {
        var drive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var hibernation = FileSize(Path.Join(drive, "hiberfil.sys"));
        var pageFiles = new[] { FileSize(Path.Join(drive, "pagefile.sys")), FileSize(Path.Join(drive, "swapfile.sys")) };
        var previous = new[] { "Windows.old", "$Windows.~BT", "$Windows.~WS" }
            .Select(name => FolderSize(Path.Join(drive, name), scan, cancellationToken))
            .ToList();
        var updates = FolderSize(Path.Join(windows, "SoftwareDistribution", "Download"), scan, cancellationToken);
        var components = FolderSize(Path.Join(windows, "WinSxS"), scan, cancellationToken);

        return
        [
            new(SystemSpaceKind.Hibernation, hibernation, hibernation is not null),
            new(SystemSpaceKind.PageFile, Sum(pageFiles), pageFiles.Any(p => p is not null)),
            new(SystemSpaceKind.PreviousWindows, Sum(previous), previous.Any(p => p is not null)),
            new(SystemSpaceKind.UpdateDownloads, updates, updates is not null),
            new(SystemSpaceKind.ComponentStore, components, components is not null),

            // Shadow storage can only be queried as administrator, so its size stays unknown here.
            new(SystemSpaceKind.RestorePoints, null, Exists: true)
        ];
    }

    private static long? Sum(IEnumerable<long?> sizes)
    {
        var known = sizes.Where(s => s is not null).ToList();
        return known.Count == 0 ? null : known.Sum();
    }

    private static long? FileSize(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long? FolderSize(string path, ScanResult? scan, CancellationToken cancellationToken)
    {
        if (scan?.Root.Find(path) is FolderNode { IsUnreadable: false } folder)
        {
            return folder.Size;
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        long total = 0;
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", Everything))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += file.Length;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Whatever could be read counts; Windows keeps parts of these folders private.
        }

        return total;
    }
}
