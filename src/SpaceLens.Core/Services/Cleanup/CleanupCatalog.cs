using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Scanning;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>
/// What SpaceLens knows about Windows storage: which locations are caches, crash reports or system leftovers.
/// Shared by the risk analyzer (one item) and the cleanup analyzer (the whole scan).
/// </summary>
public sealed class CleanupCatalog
{
    /// <summary>Installers younger than this may not have been run yet.</summary>
    public static readonly TimeSpan RecentDownloadAge = TimeSpan.FromDays(14);

    /// <summary>Temp entries younger than this may belong to an app that is still running.</summary>
    public static readonly TimeSpan TempGraceAge = TimeSpan.FromDays(2);

    public static readonly TimeSpan StaleFileAge = TimeSpan.FromDays(365);

    public const long LargeVideoBytes = 500L * 1024 * 1024;
    public const long StaleLargeFileBytes = 1024L * 1024 * 1024;

    /// <summary>Folder names that apps (browsers, Electron apps, GPU drivers) use for rebuildable caches.</summary>
    private static readonly HashSet<string> CacheFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache", "GraphiteDawnCache",
        "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "CachedData", "cache2", "CacheStorage"
    };

    private static readonly HashSet<string> SystemRootFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information", "Recovery", "Boot", "EFI", "$WinREAgent", "$SysReset", "$GetCurrent", "Config.Msi", "PerfLogs"
    };

    private readonly string[] _browserRoots;
    private readonly string[] _cacheFolders;
    private readonly string[] _crashFolders;

    public CleanupCatalog(KnownLocations locations)
    {
        Locations = locations;
        _browserRoots =
        [
            Path.Join(locations.LocalAppData, "Google", "Chrome"),
            Path.Join(locations.LocalAppData, "Microsoft", "Edge"),
            Path.Join(locations.LocalAppData, "BraveSoftware"),
            Path.Join(locations.LocalAppData, "Mozilla", "Firefox"),
            Path.Join(locations.LocalAppData, "Vivaldi"),
            Path.Join(locations.LocalAppData, "Chromium"),
            Path.Join(locations.LocalAppData, "Opera Software"),
            Path.Join(locations.RoamingAppData, "Opera Software")
        ];
        _cacheFolders =
        [
            Path.Join(locations.LocalAppData, "npm-cache"),
            Path.Join(locations.RoamingAppData, "npm-cache"),
            Path.Join(locations.LocalAppData, "pip", "cache"),
            Path.Join(locations.LocalAppData, "Yarn", "Cache"),
            Path.Join(locations.LocalAppData, "NuGet", "v3-cache"),
            Path.Join(locations.LocalAppData, "NuGet", "plugins-cache"),
            Path.Join(locations.LocalAppData, "go-build"),
            Path.Join(locations.LocalAppData, "D3DSCache"),
            Path.Join(locations.LocalAppData, "NVIDIA", "DXCache"),
            Path.Join(locations.LocalAppData, "NVIDIA", "GLCache"),
            Path.Join(locations.LocalAppData, "AMD", "DxCache"),
            Path.Join(locations.LocalAppData, "AMD", "DxcCache"),
            Path.Join(locations.LocalAppData, "Microsoft", "Windows", "INetCache"),
            Path.Join(locations.UserProfile, ".gradle", "caches"),
            Path.Join(locations.UserProfile, ".nuget", "packages")
        ];
        _crashFolders =
        [
            Path.Join(locations.LocalAppData, "CrashDumps"),
            Path.Join(locations.LocalAppData, "Microsoft", "Windows", "WER", "ReportArchive"),
            Path.Join(locations.LocalAppData, "Microsoft", "Windows", "WER", "ReportQueue")
        ];
    }

    public KnownLocations Locations { get; }

    public bool IsTempEntry(string path) =>
        PathRules.IsInside(path, Locations.Temp) || PathRules.IsInside(path, Path.Join(Locations.Windows, "Temp"));

    public bool IsBrowserCache(string path) =>
        _browserRoots.Any(root => PathRules.IsInside(path, root)) && HasCacheSegment(path);

    /// <summary>A cache folder name somewhere inside AppData, e.g. an Electron app's "Code Cache".</summary>
    public bool IsAppCache(string path) => PathRules.IsInside(path, Locations.AppData) && HasCacheSegment(path);

    public bool IsKnownCacheFolder(string path) => _cacheFolders.Any(folder => PathRules.IsSameOrInside(path, folder));

    public bool IsCrashReport(string path) => _crashFolders.Any(folder => PathRules.IsSameOrInside(path, folder));

    public bool IsCacheFolderName(string name) => CacheFolderNames.Contains(name);

    public bool IsSystemRootFolder(string name) => SystemRootFolders.Contains(name);

    /// <summary>
    /// Space only Windows should reclaim: a previous installation, upgrade staging or update downloads
    /// (the folder itself or anything inside it).
    /// </summary>
    public bool IsWindowsLeftover(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        return PathRules.IsSameOrInside(path, Path.Join(root, "Windows.old"))
            || PathRules.IsSameOrInside(path, Path.Join(root, "$Windows.~BT"))
            || PathRules.IsSameOrInside(path, Path.Join(root, "$Windows.~WS"))
            || PathRules.IsSameOrInside(path, Path.Join(Locations.Windows, "SoftwareDistribution", "Download"));
    }

    /// <summary>
    /// A setup file saved straight into Downloads. Programs in subfolders (an extracted portable app, say)
    /// don't count: they may be the app itself.
    /// </summary>
    public bool IsDownloadedInstaller(FileNode file)
    {
        var isSetupFile = FileTypeClassifier.GetKind(file.Extension) == FileKind.Installer
            || file.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
        return isSetupFile && file.Parent is { } folder && PathRules.IsSame(folder.FullPath, Locations.Downloads);
    }

    public bool IsInPersonalArea(string path) =>
        PathRules.IsInside(path, Locations.UserProfile) && !PathRules.IsSameOrInside(path, Locations.AppData) && !IsToolData(path);

    /// <summary>
    /// Hidden folders directly in the user profile (.vscode, .local, .cargo, ...) hold tools' settings and binaries,
    /// so they're treated like AppData rather than personal files.
    /// </summary>
    public bool IsToolData(string path)
    {
        if (!PathRules.IsInside(path, Locations.UserProfile))
        {
            return false;
        }

        var relative = path[Path.TrimEndingDirectorySeparator(Locations.UserProfile).Length..].TrimStart('\\');
        return relative.StartsWith('.');
    }

    /// <summary>Dependency and build-output folders that a project's tools recreate (node_modules, bin/obj, target, venv).</summary>
    public static bool IsRebuildableFolder(FolderNode folder) => folder.Name.ToLowerInvariant() switch
    {
        "node_modules" => true,
        "bin" or "obj" => HasFileEndingWith(folder.Parent, ".csproj", ".fsproj", ".vbproj"),
        "target" => HasFileEndingWith(folder.Parent, "Cargo.toml"),
        ".venv" or "venv" => HasFileEndingWith(folder, "pyvenv.cfg"),
        _ => false
    };

    private static bool HasFileEndingWith(FolderNode? folder, params string[] suffixes) =>
        folder?.Files.Any(f => suffixes.Any(s => f.Name.EndsWith(s, StringComparison.OrdinalIgnoreCase))) == true;

    private bool HasCacheSegment(string path)
    {
        foreach (var segment in path.Split('\\'))
        {
            if (CacheFolderNames.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }
}
