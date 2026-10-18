using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Apps;

/// <summary>How much space an installed app uses: measured from the scan when it covers the app's folder, or on disk.</summary>
public static class AppSize
{
    private static readonly EnumerationOptions Everything = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    /// <summary>The size of the app's folder in <paramref name="scan"/>, or null when the scan doesn't include it.</summary>
    public static long? Measure(InstalledApp app, ScanResult? scan) =>
        app.InstallFolder is { } location && scan?.Root.Find(location) is FolderNode { Size: > 0 } folder ? folder.Size : null;

    /// <summary>Measured when possible, otherwise what the installer reported (0 when neither is known).</summary>
    public static long Best(InstalledApp app, ScanResult? scan) => Measure(app, scan) ?? app.SizeBytes ?? 0;

    /// <summary>
    /// Adds up the files in an app folder the scan doesn't cover, such as an app on another drive. Links aren't followed
    /// and unreadable parts are skipped. Null when the folder doesn't exist.
    /// </summary>
    public static long? MeasureOnDisk(string folder, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        long total = 0;
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", Everything))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += file.Length;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Whatever could be read counts.
        }

        return total;
    }
}
