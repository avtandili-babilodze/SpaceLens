using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SpaceLens.Core.Configuration;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Integration;
using SpaceLens.Core.Services.Interaction;
using SpaceLens.Core.Services.Settings;

namespace SpaceLens.Infrastructure;

/// <summary>
/// Installs the single-file SpaceLens.exe for the current user: a copy in %LOCALAPPDATA%\Programs\SpaceLens, desktop and
/// Start menu shortcuts, the right-click menu entry and an entry in Settings › Apps. No administrator rights needed.
/// A downloaded copy offers to install itself when it starts. Uninstalling removes all of that but keeps scan history
/// and settings.
/// </summary>
public sealed class Installer(
    IShellIntegration shell,
    IDailyCheckScheduler scheduler,
    IConfirmationService dialogs,
    ISettingsStore settings,
    AppPaths paths,
    ILogger<Installer> logger)
{
    public static string InstallFolder =>
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SpaceLens");

    public static string InstalledPath => Path.Join(InstallFolder, "SpaceLens.exe");

    public static bool IsRunningInstalledCopy => Environment.ProcessPath is { } path && PathRules.IsSame(path, InstalledPath);

    /// <summary>A packaged build carries everything inside SpaceLens.exe; a development build has its DLLs next to it.</summary>
    public static bool CanInstall =>
        Environment.ProcessPath is not null && !File.Exists(Path.Join(AppContext.BaseDirectory, "SpaceLens.Core.dll"));

    public static string Version => typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Installs (or updates) SpaceLens. True when the installed copy is ready to start.</summary>
    public bool Install()
    {
        if (!CanInstall)
        {
            dialogs.Inform(
                "This copy can't be installed",
                "Install works from the packaged SpaceLens.exe (made with package.cmd). This is a development build.");
            return false;
        }

        try
        {
            if (!IsRunningInstalledCopy)
            {
                Directory.CreateDirectory(InstallFolder);
                File.Copy(Environment.ProcessPath!, InstalledPath, overwrite: true);
                Unblock(InstalledPath);
            }

            shell.CreateShortcuts(InstalledPath);
            shell.RegisterContextMenu(InstalledPath);
            shell.RegisterUninstallEntry(InstalledPath, Version);

            // A daily check set up from a portable copy should run the installed one from now on.
            if (scheduler.IsEnabled)
            {
                scheduler.Enable(InstalledPath);
            }

            logger.LogInformation("Installed SpaceLens {Version} to {Folder}", Version, InstallFolder);
            return true;
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Couldn't copy SpaceLens to {Folder}", InstallFolder);
            dialogs.Inform(
                "Close SpaceLens first",
                "The installed SpaceLens is open, so it can't be replaced. Close it, then run the installer again.");
            return false;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            logger.LogWarning(exception, "Installing SpaceLens failed");
            dialogs.Inform("SpaceLens couldn't be installed", exception.Message);
            return false;
        }
    }

    /// <summary>Another SpaceLens window started from the installed folder (this process doesn't count).</summary>
    private static bool IsInstalledCopyOpenElsewhere()
    {
        var current = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName("SpaceLens"))
        {
            using (process)
            {
                try
                {
                    if (process.Id != current && process.MainModule?.FileName is { } path && PathRules.IsSame(path, InstalledPath))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Gone already, or not ours to inspect.
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The copy keeps the downloaded-from-the-internet mark of the zip it came from, which would make Windows warn
    /// every time the shortcut is used. The user already chose to run it, so the installed copy drops the mark.
    /// </summary>
    private void Unblock(string path)
    {
        try
        {
            File.Delete(path + ":Zone.Identifier");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            logger.LogInformation(exception, "Couldn't remove the download mark from {Path}", path);
        }
    }

    /// <summary>Installs from a portable copy that's running, then hands over to the installed copy. True when it did.</summary>
    public bool InstallAndSwitch()
    {
        if (!Install())
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(InstalledPath, "--welcome") { UseShellExecute = false });
        System.Windows.Application.Current.Shutdown();
        return true;
    }

    /// <summary>
    /// Started from a downloaded or freshly built SpaceLens.exe, offers to install it, or to replace an installed copy
    /// that is a different build, so the desktop and Start menu always open the same SpaceLens. "Not now" isn't asked
    /// again for the same build. True when SpaceLens is handing over to the installed copy and this one is closing.
    /// </summary>
    public async Task<bool> OfferInstallAsync()
    {
        if (!CanInstall || IsRunningInstalledCopy)
        {
            return false;
        }

        var (build, isSameAsInstalled) = await Task.Run(() => CompareWithInstalled(Environment.ProcessPath!));
        if (isSameAsInstalled || settings.Current.SkippedInstallBuild == build)
        {
            return false;
        }

        var request = File.Exists(InstalledPath)
            ? new ConfirmationRequest(
                "Update the installed SpaceLens?",
                "The SpaceLens your desktop and Start menu open is a different version. Replace it with this one?",
                "Update")
            : new ConfirmationRequest(
                "Install SpaceLens on this PC?",
                "Adds SpaceLens to your desktop and Start menu, and Analyze with SpaceLens to File Explorer's right-click menu. "
                    + "Only your Windows account is changed; no administrator rights are needed.",
                "Install",
                Footnote: "You can also install it later in Settings.");
        if (dialogs.Confirm(request with { CancelText = "Not now" }))
        {
            return InstallAndSwitch();
        }

        settings.Save(settings.Current with { SkippedInstallBuild = build });
        return false;
    }

    /// <summary>This build's SHA-256, and whether the installed copy is the same file (only hashed when the sizes match).</summary>
    private static (string Build, bool IsSameAsInstalled) CompareWithInstalled(string path)
    {
        var build = Hash(path);
        try
        {
            var installed = new FileInfo(InstalledPath);
            var isSame = installed.Exists && installed.Length == new FileInfo(path).Length && Hash(InstalledPath) == build;
            return (build, isSame);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (build, false);
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Asks first, then removes everything Install added. True when it was removed.</summary>
    public bool Uninstall()
    {
        if (IsInstalledCopyOpenElsewhere())
        {
            dialogs.Inform("Close SpaceLens first", "SpaceLens is open, so it can't be removed. Close it, then uninstall again.");
            return false;
        }

        var confirmed = dialogs.Confirm(new ConfirmationRequest(
            "Uninstall SpaceLens?",
            "This removes SpaceLens, its shortcuts, the right-click menu entry and the daily check.",
            "Uninstall",
            Footnote: $"Your scan history and settings stay in {paths.Root}. Delete that folder too if you won't use SpaceLens again.",
            IsDestructive: true));
        if (!confirmed)
        {
            return false;
        }

        try
        {
            shell.RemoveShortcuts();
            shell.UnregisterContextMenu();
            scheduler.Disable();
            shell.RemoveUninstallEntry();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Part of the uninstall failed");
        }

        // A running program can't delete itself; a hidden command removes the folder a moment after SpaceLens exits.
        if (Directory.Exists(InstallFolder))
        {
            // cmd.exe reads the rest of its command line as-is, so the path is quoted for cmd, not with ArgumentList's escaping.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{InstallFolder}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        logger.LogInformation("Uninstalled SpaceLens from {Folder}", InstallFolder);
        dialogs.Inform("SpaceLens was removed", "Thanks for trying it. Your files were never changed by uninstalling.");
        return true;
    }
}
