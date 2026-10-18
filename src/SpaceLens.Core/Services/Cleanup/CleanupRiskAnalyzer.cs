using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Settings;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>
/// Rules are checked from most to least protective, so a cache inside Windows stays protected
/// and nothing large is ever called safe just because of its size.
/// </summary>
public sealed class CleanupRiskAnalyzer(CleanupCatalog catalog, TimeProvider clock, ISettingsStore settings) : ICleanupRiskAnalyzer
{
    private static readonly RiskAssessment Excluded = new(CleanupRisk.Protected,
        "You asked SpaceLens never to suggest this folder for cleanup (Settings › Never-touch folders).");

    private static readonly RiskAssessment Link = new(CleanupRisk.Protected,
        "Link or junction. SpaceLens doesn't follow links, so its space is counted where the target lives.");

    private static readonly RiskAssessment WindowsFiles = new(CleanupRisk.Protected,
        "Part of Windows. Removing it can stop Windows from starting. Use Settings › System › Storage to clean up safely.");

    private static readonly RiskAssessment ProgramFiles = new(CleanupRisk.Protected,
        "Installed program files. Uninstall the app from Settings › Apps instead of deleting its files.");

    private static readonly RiskAssessment SharedProgramData = new(CleanupRisk.Protected,
        "Shared data that installed programs rely on. Remove it by uninstalling the program that owns it.");

    private static readonly RiskAssessment WindowsLeftover = new(CleanupRisk.Protected,
        "Windows update or upgrade leftovers. Remove them with Settings › System › Storage › Temporary files.");

    private static readonly RiskAssessment SystemFolder = new(CleanupRisk.Protected,
        "System folder (restore points, recovery or boot files). Windows manages it.");

    private static readonly RiskAssessment OwnData = new(CleanupRisk.Protected, "SpaceLens' own settings and scan history.");

    private static readonly RiskAssessment RecycleBin = new(CleanupRisk.Low,
        "Items you already deleted. Empty the Recycle Bin to free this space.");

    private static readonly RiskAssessment TempFolder = new(CleanupRisk.High,
        "The Temp folder itself. Apps expect it to exist; remove what's inside instead.");

    private static readonly RiskAssessment TempFile = new(CleanupRisk.Low,
        "Temporary file. Apps recreate what they need, and anything still in use is skipped.");

    private static readonly RiskAssessment RecentTempFile = new(CleanupRisk.Review,
        "Temporary file written in the last two days. An app that is still running may need it.");

    private static readonly RiskAssessment BrowserCache = new(CleanupRisk.Low,
        "Browser cache. The browser rebuilds it as you browse; close the browser first.");

    private static readonly RiskAssessment AppCache = new(CleanupRisk.Low,
        "App cache that's downloaded or rebuilt automatically. Close the app first.");

    private static readonly RiskAssessment CrashReport = new(CleanupRisk.Low,
        "Crash dump or error report. Only useful for diagnosing a past crash.");

    private static readonly RiskAssessment OldInstaller = new(CleanupRisk.Low,
        "Setup file you downloaded a while ago. Most installers can be downloaded again if you need them.");

    private static readonly RiskAssessment RecentInstaller = new(CleanupRisk.Review,
        "Setup file downloaded recently, so you may not have installed it yet.");

    private static readonly RiskAssessment OtherAccount = new(CleanupRisk.High,
        "Files and settings of another account on this PC. Only that user should decide what to remove.");

    private static readonly RiskAssessment UsersFolder = new(CleanupRisk.High,
        "Contains every user account's files and settings. Never delete the whole folder.");

    private static readonly RiskAssessment ProfileFolder = new(CleanupRisk.High,
        "Your user profile with your documents, settings and app data. Never delete the whole folder.");

    private static readonly RiskAssessment AppDataRoot = new(CleanupRisk.High,
        "Settings and data for all of your apps. Never delete the whole folder.");

    private static readonly RiskAssessment AppData = new(CleanupRisk.High,
        "App data such as settings, sign-ins, saves and offline files. Use the app's own option to clear it.");

    private static readonly RiskAssessment ToolData = new(CleanupRisk.High,
        "Settings, binaries or data of a developer tool or app (a hidden folder in your profile). Use the tool's own cleanup command.");

    private static readonly RiskAssessment PersonalFolder = new(CleanupRisk.High,
        "One of your main personal folders. Review the files inside instead of removing the folder.");

    private static readonly RiskAssessment Repository = new(CleanupRisk.High,
        "Version-control history. Deleting it loses the project's history.");

    private static readonly RiskAssessment IrreplaceableData = new(CleanupRisk.High,
        "May hold data that exists nowhere else (a database, mailbox, virtual machine or password vault).");

    private static readonly RiskAssessment ProgramComponent = new(CleanupRisk.High,
        "Program component. Removing it can break the app that uses it.");

    private static readonly RiskAssessment Rebuildable = new(CleanupRisk.Review,
        "Dependencies or build output. Reinstalling packages or rebuilding the project recreates it.");

    private static readonly RiskAssessment DownloadedArchive = new(CleanupRisk.Review,
        "Archive that may be the only copy of its contents. Remove it once you've extracted or backed it up.");

    private static readonly RiskAssessment LargeVideo = new(CleanupRisk.Review,
        "Large video. Keep it unless you have a copy somewhere else.");

    private static readonly RiskAssessment PersonalFile = new(CleanupRisk.Review,
        "Personal file. Make sure it's backed up or no longer needed before removing it.");

    private static readonly RiskAssessment Folder = new(CleanupRisk.Review,
        "Folder with mixed contents. Check what's inside before removing it.");

    private static readonly RiskAssessment File = new(CleanupRisk.Review,
        "SpaceLens can't tell whether you still need this. Size alone doesn't make a file unnecessary.");

    private KnownLocations Locations => catalog.Locations;

    public RiskAssessment Assess(StorageNode node)
    {
        var path = node.FullPath;
        return AssessProtected(node, path)
            ?? AssessDisposable(node, path)
            ?? AssessValuable(node, path)
            ?? AssessReviewable(node, path);
    }

    private RiskAssessment? AssessProtected(StorageNode node, string path)
    {
        if (node is FolderNode { IsLink: true })
        {
            return Link;
        }

        if (settings.Current.ExcludedFolders.Any(folder => PathRules.IsSameOrInside(path, folder)))
        {
            return Excluded;
        }

        var driveRoot = Path.GetPathRoot(path) ?? "";
        if (PathRules.IsSame(path, driveRoot))
        {
            return new RiskAssessment(CleanupRisk.Protected, "The whole drive.");
        }

        if (PathRules.IsSameOrInside(path, Locations.AppDataFolder))
        {
            return OwnData;
        }

        if (catalog.IsWindowsLeftover(path))
        {
            return WindowsLeftover;
        }

        if (PathRules.IsInside(path, Path.Join(Locations.Windows, "Temp")))
        {
            return IsOlderThan(node, CleanupCatalog.TempGraceAge) ? TempFile : RecentTempFile;
        }

        if (PathRules.IsSameOrInside(path, Locations.Windows))
        {
            return WindowsFiles;
        }

        if (Locations.ProgramFiles.Any(folder => PathRules.IsSameOrInside(path, folder)))
        {
            return ProgramFiles;
        }

        if (PathRules.IsSameOrInside(path, Locations.ProgramData))
        {
            return SharedProgramData;
        }

        // The same folders on another drive (a second Windows installation, apps installed to D:) are just as fragile.
        var topLevel = TopLevelName(path, driveRoot);
        var systemFolder = topLevel.ToLowerInvariant() switch
        {
            "windows" => WindowsFiles,
            "program files" or "program files (x86)" => ProgramFiles,
            "programdata" => SharedProgramData,
            _ => null
        };
        if (systemFolder is not null)
        {
            return systemFolder;
        }

        if (string.Equals(topLevel, "$Recycle.Bin", StringComparison.OrdinalIgnoreCase))
        {
            return RecycleBin;
        }

        if (catalog.IsSystemRootFolder(topLevel))
        {
            return SystemFolder;
        }

        return node is FileNode && node.Parent is { IsRoot: true } parent && PathRules.IsSame(parent.FullPath, driveRoot)
            ? AssessRootFile(node.Name)
            : null;
    }

    private RiskAssessment? AssessDisposable(StorageNode node, string path)
    {
        if (PathRules.IsSame(path, Locations.Temp))
        {
            return TempFolder;
        }

        if (catalog.IsTempEntry(path))
        {
            return IsOlderThan(node, CleanupCatalog.TempGraceAge) ? TempFile : RecentTempFile;
        }

        if (catalog.IsBrowserCache(path))
        {
            return BrowserCache;
        }

        if (catalog.IsKnownCacheFolder(path) || catalog.IsAppCache(path))
        {
            return AppCache;
        }

        if (catalog.IsCrashReport(path))
        {
            return CrashReport;
        }

        if (node is not FileNode file)
        {
            return null;
        }

        if (file.Extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase) && PathRules.IsInside(path, Locations.UserProfile))
        {
            return CrashReport;
        }

        if (catalog.IsDownloadedInstaller(file))
        {
            return IsOlderThan(node, CleanupCatalog.RecentDownloadAge) ? OldInstaller : RecentInstaller;
        }

        return null;
    }

    private RiskAssessment? AssessValuable(StorageNode node, string path)
    {
        if (PathRules.IsSame(path, Locations.UsersRoot))
        {
            return UsersFolder;
        }

        if (PathRules.IsSame(path, Locations.UserProfile))
        {
            return ProfileFolder;
        }

        var usersFolder = Path.Join(Path.GetPathRoot(path), "Users");
        if (PathRules.IsSame(path, usersFolder))
        {
            return UsersFolder;
        }

        if (PathRules.IsInside(path, usersFolder) && !PathRules.IsInside(path, Locations.UserProfile))
        {
            return OtherAccount;
        }

        if (PathRules.IsSame(path, Locations.AppData) || PathRules.IsSame(Path.GetDirectoryName(path) ?? "", Locations.AppData))
        {
            return AppDataRoot;
        }

        if (PathRules.IsInside(path, Locations.AppData))
        {
            return AppData;
        }

        // ~\.cache is the one hidden folder that is a cache by convention; it falls through to Review.
        var isUserCache = PathRules.IsSameOrInside(path, Path.Join(Locations.UserProfile, ".cache"));
        if (catalog.IsToolData(path) && !isUserCache)
        {
            return ToolData;
        }

        if (Locations.PersonalFolders.Any(folder => PathRules.IsSame(path, folder)))
        {
            return PersonalFolder;
        }

        if (node.Name.Equals(".git", StringComparison.OrdinalIgnoreCase) || PathRules.HasSegment(path, ".git"))
        {
            return Repository;
        }

        if (node is FileNode file)
        {
            var kind = FileTypeClassifier.GetKind(file.Extension);
            if (kind is FileKind.Database or FileKind.VirtualDisk)
            {
                return IrreplaceableData;
            }

            // Installers saved straight to Downloads were handled above; any other program file may be in use by an app.
            if (file.Extension.ToLowerInvariant() is ".dll" or ".sys" or ".exe")
            {
                return ProgramComponent;
            }
        }

        return null;
    }

    private RiskAssessment AssessReviewable(StorageNode node, string path)
    {
        if (node is FolderNode folder)
        {
            return CleanupCatalog.IsRebuildableFolder(folder) ? Rebuildable : Folder;
        }

        var file = (FileNode)node;
        var kind = FileTypeClassifier.GetKind(file.Extension);
        if (kind == FileKind.Archive && PathRules.IsInside(path, Locations.Downloads))
        {
            return DownloadedArchive;
        }

        if (kind == FileKind.Video && file.Size >= CleanupCatalog.LargeVideoBytes)
        {
            return LargeVideo;
        }

        return Locations.PersonalFolders.Any(f => PathRules.IsInside(path, f)) ? PersonalFile : File;
    }

    public IReadOnlyList<StorageNode> FindIrreplaceableContents(FolderNode folder, int limit)
    {
        var found = new List<StorageNode>();
        foreach (var node in folder.Descendants())
        {
            var isRepository = node is FolderNode && node.Name.Equals(".git", StringComparison.OrdinalIgnoreCase);
            var isDataFile = node is FileNode file
                && FileTypeClassifier.GetKind(file.Extension) is FileKind.Database or FileKind.VirtualDisk;
            if (isRepository || isDataFile)
            {
                found.Add(node);
                if (found.Count >= limit)
                {
                    break;
                }
            }
        }

        return found;
    }

    private static RiskAssessment? AssessRootFile(string name) => name.ToLowerInvariant() switch
    {
        "hiberfil.sys" => new(CleanupRisk.Protected,
            "Hibernation file. Turning off hibernation (powercfg /h off, as administrator) removes it."),
        "pagefile.sys" or "swapfile.sys" => new(CleanupRisk.Protected,
            "Virtual memory. Windows needs it; change its size in Advanced system settings if you must."),
        "bootmgr" or "bootnxt" or "dumpstack.log" or "dumpstack.log.tmp" => new(CleanupRisk.Protected, "Windows boot or diagnostics file."),
        _ => null
    };

    private static string TopLevelName(string path, string driveRoot)
    {
        var relative = path.Length > driveRoot.Length ? path[driveRoot.Length..] : "";
        var separator = relative.IndexOf('\\');
        return separator < 0 ? relative : relative[..separator];
    }

    private bool IsOlderThan(StorageNode node, TimeSpan age) => clock.GetUtcNow().UtcDateTime - node.LastWriteUtc > age;
}
