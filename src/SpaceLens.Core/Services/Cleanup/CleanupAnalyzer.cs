using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Scanning;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>
/// Walks a scan once and sorts every item that matches a cleanup rule into its group. A folder that matches is
/// taken whole and not searched further, so nothing is counted twice.
/// </summary>
public sealed class CleanupAnalyzer(
    CleanupCatalog catalog,
    ICleanupRiskAnalyzer riskAnalyzer,
    IRecycleBinService recycleBin,
    TimeProvider clock) : ICleanupAnalyzer
{
    private static readonly Dictionary<CleanupKind, OpportunityTemplate> Templates = new OpportunityTemplate[]
    {
        new(CleanupKind.WindowsLeftovers, "Windows update leftovers",
            "A previous Windows installation and update downloads. Windows removes them safely from Storage settings.",
            CleanupRisk.Low, CleanupAction.OpenStorageSettings),
        new(CleanupKind.TempFiles, "Temporary files",
            "Leftovers apps wrote to the Temp folder more than two days ago. Anything still in use is skipped.",
            CleanupRisk.Low),
        new(CleanupKind.BrowserCache, "Browser caches",
            "Cached web pages and images. Browsers rebuild them as you browse; close the browser first.",
            CleanupRisk.Low),
        new(CleanupKind.AppCache, "App and developer caches",
            "Package-manager, graphics and app caches that are downloaded or rebuilt automatically.",
            CleanupRisk.Low),
        new(CleanupKind.CrashReports, "Crash dumps and error reports",
            "Only useful for diagnosing a crash that already happened.",
            CleanupRisk.Low),
        new(CleanupKind.OldInstallers, "Old installers",
            "Setup files saved to Downloads more than two weeks ago. Most can be downloaded again.",
            CleanupRisk.Low),
        new(CleanupKind.DownloadedArchives, "Downloaded archives",
            "ZIP, RAR and 7z files in Downloads. Keep any whose contents you haven't extracted or backed up.",
            CleanupRisk.Review),
        new(CleanupKind.DiskImages, "Disk images",
            "ISO and other disc images, usually installation media you can download again.",
            CleanupRisk.Review),
        new(CleanupKind.LargeVideos, "Large videos",
            "Video files of 500 MB or more in your personal folders. Keep anything you haven't backed up.",
            CleanupRisk.Review),
        new(CleanupKind.DevArtifacts, "Build outputs and dependencies",
            "node_modules, bin/obj and target folders that your build tools can recreate.",
            CleanupRisk.Review),
        new(CleanupKind.StaleLargeFiles, "Large files you haven't changed in a year",
            "Files of 1 GB or more that haven't been modified for over a year.",
            CleanupRisk.Review)
    }.ToDictionary(t => t.Kind);

    public CleanupAnalysis Analyze(ScanResult scan, CancellationToken cancellationToken = default)
    {
        var matches = Templates.Keys.ToDictionary(kind => kind, _ => new List<StorageNode>());
        var now = clock.GetUtcNow().UtcDateTime;

        var pending = new Stack<FolderNode>();
        pending.Push(scan.Root);
        var visited = 0;
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            if (++visited % 512 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            foreach (var child in folder.Folders)
            {
                var kind = MatchFolder(child);
                if (kind is { } match && IsEligible(child, match))
                {
                    matches[match].Add(child);
                }
                else if (!child.IsLink)
                {
                    pending.Push(child);
                }
            }

            foreach (var file in folder.Files)
            {
                var kind = MatchFile(file, now);
                if (kind is { } match && file.Size > 0 && IsEligible(file, match))
                {
                    matches[match].Add(file);
                }
            }
        }

        var opportunities = Templates.Values
            .Select(template => template.Create(matches[template.Kind].OrderByDescending(n => n.Size).ToList()))
            .Where(o => o.Bytes > 0)
            .ToList();

        if (scan.IsDriveRoot && RecycleBinOpportunity(scan) is { } recycleBinGroup)
        {
            opportunities.Add(recycleBinGroup);
        }

        return new CleanupAnalysis
        {
            Opportunities = opportunities.OrderBy(o => o.Risk).ThenByDescending(o => o.Bytes).ToList()
        };
    }

    private CleanupOpportunity? RecycleBinOpportunity(ScanResult scan)
    {
        var bin = recycleBin.Query(scan.RootPath);
        if (bin.Bytes <= 0)
        {
            return null;
        }

        return new CleanupOpportunity
        {
            Kind = CleanupKind.RecycleBin,
            Title = "Recycle Bin",
            Description = $"{bin.ItemCount:N0} items you already deleted. Emptying the Recycle Bin frees this space for good.",
            Risk = CleanupRisk.Low,
            Action = CleanupAction.EmptyRecycleBin,
            KnownBytes = bin.Bytes
        };
    }

    private CleanupKind? MatchFolder(FolderNode folder)
    {
        var path = folder.FullPath;
        if (catalog.IsWindowsLeftover(path))
        {
            return CleanupKind.WindowsLeftovers;
        }

        if (catalog.IsTempEntry(path))
        {
            return IsOlderThan(folder, CleanupCatalog.TempGraceAge) ? CleanupKind.TempFiles : null;
        }

        if (catalog.IsBrowserCache(path) && catalog.IsCacheFolderName(folder.Name))
        {
            return CleanupKind.BrowserCache;
        }

        var isNamedCache = catalog.IsAppCache(path) && catalog.IsCacheFolderName(folder.Name);
        if (catalog.IsKnownCacheFolder(path) || isNamedCache)
        {
            return CleanupKind.AppCache;
        }

        if (catalog.IsCrashReport(path))
        {
            return CleanupKind.CrashReports;
        }

        return CleanupCatalog.IsRebuildableFolder(folder) ? CleanupKind.DevArtifacts : null;
    }

    private CleanupKind? MatchFile(FileNode file, DateTime now)
    {
        var path = file.FullPath;
        if (catalog.IsTempEntry(path))
        {
            return IsOlderThan(file, CleanupCatalog.TempGraceAge) ? CleanupKind.TempFiles : null;
        }

        var isDumpFile = file.Extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase)
            && PathRules.IsInside(path, catalog.Locations.UserProfile);
        if (isDumpFile || catalog.IsCrashReport(path))
        {
            return CleanupKind.CrashReports;
        }

        if (catalog.IsDownloadedInstaller(file))
        {
            return IsOlderThan(file, CleanupCatalog.RecentDownloadAge) ? CleanupKind.OldInstallers : null;
        }

        var kind = FileTypeClassifier.GetKind(file.Extension);
        var isOldDownloadedArchive = kind == FileKind.Archive
            && PathRules.IsInside(path, catalog.Locations.Downloads)
            && IsOlderThan(file, CleanupCatalog.RecentDownloadAge);
        if (isOldDownloadedArchive)
        {
            return CleanupKind.DownloadedArchives;
        }

        if (!catalog.IsInPersonalArea(path))
        {
            return null;
        }

        if (kind == FileKind.DiskImage)
        {
            return CleanupKind.DiskImages;
        }

        if (kind == FileKind.Video && file.Size >= CleanupCatalog.LargeVideoBytes)
        {
            return CleanupKind.LargeVideos;
        }

        var isStale = file.Size >= CleanupCatalog.StaleLargeFileBytes && now - file.LastWriteUtc > CleanupCatalog.StaleFileAge;
        return isStale ? CleanupKind.StaleLargeFiles : null;
    }

    /// <summary>Second line of defence: an item the risk analyzer protects is never offered, whatever rule matched it.</summary>
    private bool IsEligible(StorageNode node, CleanupKind kind) =>
        Templates[kind].Action != CleanupAction.RecycleItems || riskAnalyzer.Assess(node).AllowsRemoval;

    private bool IsOlderThan(StorageNode node, TimeSpan age) => clock.GetUtcNow().UtcDateTime - node.LastWriteUtc > age;

    private sealed record OpportunityTemplate(
        CleanupKind Kind,
        string Title,
        string Description,
        CleanupRisk Risk,
        CleanupAction Action = CleanupAction.RecycleItems)
    {
        public CleanupOpportunity Create(IReadOnlyList<StorageNode> items) => new()
        {
            Kind = Kind,
            Title = Title,
            Description = Description,
            Risk = Risk,
            Action = Action,
            Items = items
        };
    }
}
