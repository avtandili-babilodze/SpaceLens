using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SpaceLens.Core.Interop;

namespace SpaceLens.Core.Services.Integration;

/// <summary>
/// Everything lives under the current user (HKCU and the user's own desktop and Start menu), so nothing needs
/// administrator rights and other accounts on the PC are untouched. On Windows 11 the right-click entry appears
/// under "Show more options".
/// </summary>
public sealed class ShellIntegration : IShellIntegration
{
    private const string MenuText = "Analyze with SpaceLens";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SpaceLens";
    private const string ShortcutName = "SpaceLens.lnk";

    /// <summary>Folders and drives get the clicked item as %1; a folder's background gets the open folder as %V.</summary>
    private static readonly (string Key, string Argument)[] MenuKeys =
    [
        (@"Software\Classes\Directory\shell\SpaceLens", "%1"),
        (@"Software\Classes\Drive\shell\SpaceLens", "%1"),
        (@"Software\Classes\Directory\Background\shell\SpaceLens", "%V")
    ];

    public string? ContextMenuTarget
    {
        get
        {
            using var command = Registry.CurrentUser.OpenSubKey(MenuKeys[0].Key + @"\command");
            var value = command?.GetValue(null) as string;
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var end = value.IndexOf('"', 1);
            return value.StartsWith('"') && end > 0 ? value[1..end] : value;
        }
    }

    public void RegisterContextMenu(string executablePath)
    {
        foreach (var (key, argument) in MenuKeys)
        {
            using var menu = Registry.CurrentUser.CreateSubKey(key);
            menu.SetValue(null, MenuText);
            menu.SetValue("Icon", $"\"{executablePath}\",0");
            using var command = menu.CreateSubKey("command");
            command.SetValue(null, $"\"{executablePath}\" --scan \"{argument}\"");
        }
    }

    public void UnregisterContextMenu()
    {
        foreach (var (key, _) in MenuKeys)
        {
            Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
        }
    }

    public void CreateShortcuts(string executablePath)
    {
        foreach (var path in ShortcutPaths())
        {
            CreateShortcut(path, executablePath);
        }

        RefreshIcons([executablePath, .. ShortcutPaths()]);
    }

    public void RemoveShortcuts()
    {
        foreach (var path in ShortcutPaths().Where(File.Exists))
        {
            File.Delete(path);
        }
    }

    public void RegisterUninstallEntry(string executablePath, string version)
    {
        var folder = Path.GetDirectoryName(executablePath) ?? "";
        var sizeKilobytes = (int)Math.Min(int.MaxValue, new FileInfo(executablePath).Length / 1024);
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "SpaceLens");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "SpaceLens");
        key.SetValue("DisplayIcon", $"\"{executablePath}\",0");
        key.SetValue("InstallLocation", folder);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        key.SetValue("UninstallString", $"\"{executablePath}\" --uninstall");
        key.SetValue("EstimatedSize", sizeKilobytes, RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public void RemoveUninstallEntry() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

    private static IEnumerable<string> ShortcutPaths() =>
    [
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName),
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName)
    ];

    /// <summary>
    /// Explorer keeps icons it has drawn in a cache, so a replaced SpaceLens.exe could still show an older icon on the
    /// desktop. Telling it these files changed, and that icons may have, makes it draw them again.
    /// </summary>
    private static void RefreshIcons(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            NativeMethods.SHChangeNotify(NativeMethods.ShcneUpdateItem, NativeMethods.ShcnfPathW | NativeMethods.ShcnfFlush, path, IntPtr.Zero);
        }

        NativeMethods.SHChangeNotify(NativeMethods.ShcneAssocChanged, NativeMethods.ShcnfIdList, null, IntPtr.Zero);
    }

    /// <summary>Uses the Windows Script Host shell object, which writes standard .lnk files.</summary>
    private static void CreateShortcut(string shortcutPath, string executablePath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows can't create shortcuts on this PC (Windows Script Host is turned off).");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            try
            {
                shortcut.TargetPath = executablePath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath);
                shortcut.IconLocation = executablePath + ",0";
                shortcut.Description = "See what's filling your drive and free space safely";
                shortcut.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
