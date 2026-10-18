using SpaceLens.Core.Services.Cleanup;

namespace SpaceLens.Core.Services.Apps;

/// <summary>
/// Works out the folder an installed app occupies, so its size can be measured. Many installers record it as
/// InstallLocation; for the rest, the uninstaller and the app's program usually sit inside it.
/// </summary>
public static class AppInstallFolder
{
    // Folders directly in Program Files that hold many programs, so they never stand for a single app.
    private static readonly HashSet<string> SharedProgramFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Common Files", "Microsoft", "WindowsApps", "ModifiableWindowsApps", "InstallShield Installation Information",
        "Windows Defender", "Windows Defender Advanced Threat Protection", "Windows Mail", "Windows Media Player",
        "Windows NT", "Windows Photo Viewer", "Windows Portable Devices", "Windows Security", "WindowsPowerShell",
        "Internet Explorer", "Reference Assemblies", "MSBuild", "dotnet", "Microsoft SDKs", "Windows Kits"
    };

    /// <summary>
    /// The folder to measure, and whether it was worked out rather than recorded by the installer. The folder is null
    /// when neither points at a folder of the app's own (a drive, Program Files itself, Windows).
    /// </summary>
    public static (string? Folder, bool IsGuessed) Find(string? installLocation, string? iconPath, string? uninstallCommand)
    {
        if (Clean(installLocation) is { } recorded && IsOwnFolder(recorded))
        {
            return (recorded, false);
        }

        var guessed = Guess(IconExecutable(iconPath), UninstallerExecutable(uninstallCommand));
        return guessed is not null && IsOwnFolder(guessed) ? (guessed, true) : (null, false);
    }

    /// <summary>The executable in a DisplayIcon value such as "C:\App\app.exe,0" or "\"C:\App\app.exe\"".</summary>
    public static string? IconExecutable(string? iconPath)
    {
        var path = Clean(iconPath);
        var comma = path?.LastIndexOf(',') ?? -1;
        if (path is not null && comma > 0 && int.TryParse(path[(comma + 1)..], out _))
        {
            path = Clean(path[..comma]);
        }

        return path is not null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    /// <summary>
    /// The deepest folder holding both programs: an uninstaller in D:\obs-studio and the program in
    /// D:\obs-studio\bin\64bit give D:\obs-studio. In Program Files the app's own top-level folder is used, because
    /// uninstallers often sit in a setup subfolder (C:\Program Files\Microsoft SQL Server\160\Setup Bootstrap\...).
    /// </summary>
    internal static string? Guess(string? programPath, string? uninstallerPath)
    {
        var folders = new[] { programPath, uninstallerPath }
            .OfType<string>()
            .Where(path => Path.IsPathFullyQualified(path) && !IsInstallerCache(path))
            .Select(Path.GetDirectoryName)
            .OfType<string>()
            .ToList();
        if (folders.Count == 0)
        {
            return null;
        }

        var folder = folders.Skip(1).Aggregate((string?)folders[0], (common, next) => common is null ? null : CommonFolder(common, next));
        return folder is null ? null : WidenToProgramFolder(folder);
    }

    private static string? UninstallerExecutable(string? uninstallCommand)
    {
        if (string.IsNullOrWhiteSpace(uninstallCommand))
        {
            return null;
        }

        var (fileName, _) = UninstallCommandLine.Split(uninstallCommand);
        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fileName : null;
    }

    /// <summary>Windows and installer caches hold copies of setup programs, not the installed app.</summary>
    private static bool IsInstallerCache(string path) =>
        PathRules.IsSameOrInside(path, Environment.GetFolderPath(Environment.SpecialFolder.Windows))
        || PathRules.IsInside(path, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
        || PathRules.HasSegment(path, "Package Cache");

    private static string? CommonFolder(string first, string second)
    {
        var a = Path.TrimEndingDirectorySeparator(first).Split('\\');
        var b = Path.TrimEndingDirectorySeparator(second).Split('\\');
        var shared = 0;
        while (shared < a.Length && shared < b.Length && string.Equals(a[shared], b[shared], StringComparison.OrdinalIgnoreCase))
        {
            shared++;
        }

        // Folders on different drives share nothing.
        return shared == 0 ? null : string.Join('\\', a[..shared]) + (shared == 1 ? "\\" : "");
    }

    private static string WidenToProgramFolder(string folder)
    {
        foreach (var root in ProgramRoots())
        {
            if (!PathRules.IsInside(folder, root))
            {
                continue;
            }

            var name = folder[(Path.TrimEndingDirectorySeparator(root).Length + 1)..].Split('\\')[0];
            return SharedProgramFolders.Contains(name) ? folder : Path.Join(root, name);
        }

        return folder;
    }

    /// <summary>False for folders many apps or users share: a drive, Program Files itself, Windows, a profile or AppData.</summary>
    private static bool IsOwnFolder(string folder)
    {
        if (!Path.IsPathFullyQualified(folder) || PathRules.IsSame(folder, Path.GetPathRoot(folder) ?? ""))
        {
            return false;
        }

        if (PathRules.IsSameOrInside(folder, Environment.GetFolderPath(Environment.SpecialFolder.Windows)))
        {
            return false;
        }

        var shared = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? ""
        };
        if (shared.Concat(ProgramRoots()).Any(root => root.Length > 0 && PathRules.IsSame(folder, root)))
        {
            return false;
        }

        return !ProgramRoots().Any(root => PathRules.IsSame(Path.GetDirectoryName(folder) ?? "", root)
            && SharedProgramFolders.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))));
    }

    private static IEnumerable<string> ProgramRoots() => new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        }
        .Where(root => root.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string? Clean(string? path)
    {
        var trimmed = path?.Trim().Trim('"').Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
