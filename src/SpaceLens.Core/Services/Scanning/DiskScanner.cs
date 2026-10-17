using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SpaceLens.Core.Interop;
using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Scanning;

/// <summary>
/// Scans a drive or folder into an in-memory <see cref="FolderNode"/> tree with recursive totals.
/// The directory walk itself is done by <see cref="ParallelDirectoryWalker"/>.
/// </summary>
public sealed class DiskScanner(IDriveService driveService, ILogger<DiskScanner> logger) : IDiskScanner
{
    public Task<ScanResult> ScanAsync(string path, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var rootPath = NormalizePath(path);
        return Task.Run(() => Scan(rootPath, progress, cancellationToken), CancellationToken.None);
    }

    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Choose a drive or folder to scan.", nameof(path));
        }

        var trimmed = path.Trim().Trim('"');

        // "C:" alone means "the current directory on C:", which is never what the user wants.
        if (trimmed.Length == 2 && trimmed[1] == ':')
        {
            trimmed += "\\";
        }

        var fullPath = Path.GetFullPath(trimmed);
        return IsDriveRoot(fullPath) ? fullPath : Path.TrimEndingDirectorySeparator(fullPath);
    }

    private ScanResult Scan(string rootPath, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var rootInfo = new DirectoryInfo(rootPath);
        if (!rootInfo.Exists)
        {
            throw new DirectoryNotFoundException($"\"{rootPath}\" doesn't exist or can't be opened.");
        }

        var isLink = rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint) && ReparsePoints.IsLink(rootPath);
        if (!IsDriveRoot(rootPath) && isLink)
        {
            throw new IOException($"\"{rootPath}\" is a link or junction. Scan the folder it points to instead.");
        }

        var startedUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var root = FolderNode.CreateRoot(rootPath, rootInfo.LastWriteTimeUtc);

        var walker = new ParallelDirectoryWalker(root, cancellationToken);
        walker.Run(progress);
        root.Aggregate();

        var result = new ScanResult
        {
            Root = root,
            StartedUtc = startedUtc,
            Duration = stopwatch.Elapsed,
            IsComplete = !cancellationToken.IsCancellationRequested,
            UnreadableFolderCount = walker.UnreadableFolders,
            SkippedLinkCount = walker.Links,
            Drive = driveService.TryGetDrive(rootPath)
        };

        logger.LogInformation(
            "Scanned {Root}: {Files} files, {Folders} folders, {Bytes} bytes in {Seconds:0.0}s "
                + "(complete: {Complete}, unreadable folders: {Unreadable})",
            rootPath,
            root.FileCount,
            root.FolderCount,
            root.Size,
            result.Duration.TotalSeconds,
            result.IsComplete,
            result.UnreadableFolderCount);
        return result;
    }

    private static bool IsDriveRoot(string path) => string.Equals(Path.GetPathRoot(path), path, StringComparison.OrdinalIgnoreCase);
}
