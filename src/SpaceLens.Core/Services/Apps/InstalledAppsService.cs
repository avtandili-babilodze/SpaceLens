using System.Globalization;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Cleanup;

namespace SpaceLens.Core.Services.Apps;

/// <summary>
/// Reads the same registry entries as Settings › Apps (for all users, 64- and 32-bit, and the current user), and
/// estimates when each app was last used from UserAssist, where Windows counts programs started from the Start menu,
/// the taskbar or File Explorer. Programs started some other way (a launcher, a script) aren't counted there.
/// </summary>
public sealed class InstalledAppsService(ILogger<InstalledAppsService> logger) : IInstalledAppsService
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>The UserAssist list of executables (the other UserAssist list holds shortcuts).</summary>
    private const string UserAssistKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count";

    /// <summary>Since Windows 7 each UserAssist value is 72 bytes: the run count at offset 4 and the last run at 60.</summary>
    private const int UsageRecordLength = 68;

    private static readonly string[] UpdateReleaseTypes = ["Update", "Hotfix", "Security Update", "Update Rollup", "ServicePack"];

    public IReadOnlyList<InstalledApp> GetInstalledApps()
    {
        var usage = ReadUsage();
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        var sources = new[]
        {
            (Hive: RegistryHive.LocalMachine, View: RegistryView.Registry64, MachineWide: true),
            (Hive: RegistryHive.LocalMachine, View: RegistryView.Registry32, MachineWide: true),
            (Hive: RegistryHive.CurrentUser, View: RegistryView.Default, MachineWide: false)
        };

        foreach (var (hive, view, machineWide) in sources)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(UninstallKey);
                foreach (var name in uninstall?.GetSubKeyNames() ?? [])
                {
                    using var entry = uninstall!.OpenSubKey(name);
                    if (entry is not null && Read(entry, machineWide) is { } app)
                    {
                        apps.TryAdd(app.Name + "|" + app.Version, WithUsage(app, usage));
                    }
                }
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
            {
                logger.LogWarning(exception, "Couldn't read installed programs from {Hive} ({View})", hive, view);
            }
        }

        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>ROT13, which UserAssist uses for its value names (letters only).</summary>
    internal static string Rot13(string text) => string.Create(text.Length, text, (chars, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            chars[i] = c switch
            {
                >= 'a' and <= 'z' => (char)('a' + ((c - 'a' + 13) % 26)),
                >= 'A' and <= 'Z' => (char)('A' + ((c - 'A' + 13) % 26)),
                _ => c
            };
        }
    });

    private static InstalledApp? Read(RegistryKey entry, bool machineWide)
    {
        var name = (entry.GetValue("DisplayName") as string)?.Trim();
        var isHidden = entry.GetValue("SystemComponent") is 1
            || entry.GetValue("ParentKeyName") is string
            || UpdateReleaseTypes.Contains(entry.GetValue("ReleaseType") as string, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(name) || isHidden)
        {
            return null;
        }

        var installDate = entry.GetValue("InstallDate") as string;
        var canRemove = entry.GetValue("NoRemove") is not 1;
        return new InstalledApp
        {
            Name = name,
            Publisher = (entry.GetValue("Publisher") as string)?.Trim() ?? "",
            Version = (entry.GetValue("DisplayVersion") as string)?.Trim() ?? "",
            InstalledOn = DateTime.TryParseExact(installDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null,
            SizeBytes = entry.GetValue("EstimatedSize") is int kilobytes and > 0 ? kilobytes * 1024L : null,
            InstallLocation = CleanPath(entry.GetValue("InstallLocation") as string),
            IconPath = (entry.GetValue("DisplayIcon") as string)?.Trim(),
            UninstallCommand = canRemove ? (entry.GetValue("UninstallString") as string)?.Trim() : null,
            IsMachineWide = machineWide
        };
    }

    private static string? CleanPath(string? path)
    {
        var trimmed = path?.Trim().Trim('"').Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>The executable in a DisplayIcon value such as "C:\App\app.exe,0" or "\"C:\App\app.exe\"".</summary>
    private static string? IconExecutable(string? iconPath)
    {
        var path = CleanPath(iconPath);
        var comma = path?.LastIndexOf(',') ?? -1;
        if (path is not null && comma > 0 && int.TryParse(path[(comma + 1)..], out _))
        {
            path = CleanPath(path[..comma]);
        }

        return path is not null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    /// <summary>The folders an app's own executables live in, so UserAssist entries can be matched to it.</summary>
    private static List<string> AppFolders(InstalledApp app)
    {
        var folders = new List<string>();
        if (app.InstallLocation is { } location)
        {
            folders.Add(location);
        }

        if (IconExecutable(app.IconPath) is { } executable && Path.GetDirectoryName(executable) is { } folder)
        {
            folders.Add(folder);
        }

        // A bare Program Files folder, or anything in Windows, would match unrelated programs.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return folders
            .Where(f => Path.IsPathFullyQualified(f)
                && !PathRules.IsSame(f, programFiles)
                && !PathRules.IsSame(f, programFilesX86)
                && !PathRules.IsSame(f, Path.GetPathRoot(f) ?? "")
                && !PathRules.IsSameOrInside(f, windows))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static InstalledApp WithUsage(InstalledApp app, List<ProgramUsage> usage)
    {
        var folders = AppFolders(app);
        if (folders.Count == 0)
        {
            return app;
        }

        var matches = usage.Where(u => folders.Any(f => PathRules.IsInside(u.Path, f))).ToList();
        return matches.Count == 0
            ? app
            : app with { LastUsedUtc = matches.Max(m => m.LastRunUtc), RunCount = matches.Sum(m => m.RunCount) };
    }

    /// <summary>Folders Windows writes as known-folder IDs at the start of UserAssist paths.</summary>
    private static Dictionary<string, string> KnownFolders() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["{6D809377-6AF0-444B-8957-A3773F02200E}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        ["{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ["{F1B32785-6FBA-4FCF-9D55-7B8E7F157091}"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ["{3EB685DB-65F9-4CF6-A03A-E3EF65729F3D}"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ["{62AB5D82-FDC1-4DC3-A9DD-070D1D495D97}"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ["{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}"] = Environment.SystemDirectory,
        ["{F38BF404-1D43-42F2-9305-67DE0B28FC23}"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
    };

    private List<ProgramUsage> ReadUsage()
    {
        var usage = new List<ProgramUsage>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserAssistKey);
            if (key is null)
            {
                return usage;
            }

            var knownFolders = KnownFolders();
            foreach (var valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is not byte[] { Length: >= UsageRecordLength } data)
                {
                    continue;
                }

                var path = Rot13(valueName);
                var separator = path.IndexOf('\\');
                if (separator > 0 && knownFolders.TryGetValue(path[..separator], out var folder))
                {
                    path = Path.Join(folder, path[(separator + 1)..]);
                }

                var lastRun = BitConverter.ToInt64(data, 60);
                if (Path.IsPathFullyQualified(path) && lastRun > 0)
                {
                    usage.Add(new ProgramUsage(path, DateTime.FromFileTimeUtc(lastRun), BitConverter.ToInt32(data, 4)));
                }
            }
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException
            or ArgumentOutOfRangeException)
        {
            logger.LogWarning(exception, "Couldn't read program usage from UserAssist");
        }

        return usage;
    }

    private sealed record ProgramUsage(string Path, DateTime LastRunUtc, int RunCount);
}
