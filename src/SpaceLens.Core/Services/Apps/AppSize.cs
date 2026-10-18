using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Apps;

/// <summary>How much space an installed app uses: measured from the scan when it covers the app's folder.</summary>
public static class AppSize
{
    /// <summary>The size of the app's folder in <paramref name="scan"/>, or null when the scan doesn't include it.</summary>
    public static long? Measure(InstalledApp app, ScanResult? scan) =>
        app.InstallLocation is { } location && scan?.Root.Find(location) is FolderNode { Size: > 0 } folder ? folder.Size : null;

    /// <summary>Measured when possible, otherwise what the installer reported (0 when neither is known).</summary>
    public static long Best(InstalledApp app, ScanResult? scan) => Measure(app, scan) ?? app.SizeBytes ?? 0;
}
