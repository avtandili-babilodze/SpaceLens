namespace SpaceLens.Core.Models;

/// <summary>The reasons an item can be suggested for cleanup. Each becomes one group on the Cleanup page.</summary>
public enum CleanupKind
{
    WindowsLeftovers,
    TempFiles,
    BrowserCache,
    AppCache,
    CrashReports,
    OldInstallers,
    RecycleBin,
    DownloadedArchives,
    DiskImages,
    LargeVideos,
    DevArtifacts,
    StaleLargeFiles
}
