namespace SpaceLens.Core.Models;

/// <summary>
/// A program listed in Windows' Apps list. <see cref="SizeBytes"/> is what the installer reported (often missing);
/// <see cref="LastUsedUtc"/> is when Windows last saw it started from the Start menu, taskbar or File Explorer.
/// </summary>
public sealed record InstalledApp
{
    public required string Name { get; init; }

    public string Publisher { get; init; } = "";

    public string Version { get; init; } = "";

    public DateTime? InstalledOn { get; init; }

    public long? SizeBytes { get; init; }

    public string? InstallLocation { get; init; }

    /// <summary>
    /// The folder the app occupies, for measuring it: <see cref="InstallLocation"/>, or when the installer didn't record
    /// one, the folder SpaceLens worked out from its uninstaller and program. Null when neither points at a folder of its own.
    /// </summary>
    public string? InstallFolder { get; init; }

    /// <summary><see cref="InstallFolder"/> was worked out rather than recorded by the installer.</summary>
    public bool IsInstallFolderGuessed { get; init; }

    /// <summary>Executable (and icon index, as "path,index") the Apps list shows as its icon.</summary>
    public string? IconPath { get; init; }

    public string? UninstallCommand { get; init; }

    public DateTime? LastUsedUtc { get; init; }

    /// <summary>How many times Windows saw it started, when it recorded that.</summary>
    public int? RunCount { get; init; }

    /// <summary>The app has an uninstaller Windows can run (some installers ask Windows not to offer one).</summary>
    public bool CanUninstall => !string.IsNullOrWhiteSpace(UninstallCommand);

    /// <summary>Installed for every user (needs administrator rights to remove) rather than just this one.</summary>
    public bool IsMachineWide { get; init; }
}
