namespace SpaceLens.Core.Services.Integration;

/// <summary>How SpaceLens shows up in Windows: File Explorer's right-click menu, shortcuts, and the Apps list.</summary>
public interface IShellIntegration
{
    /// <summary>The program "Analyze with SpaceLens" starts, or null when the menu entry isn't there.</summary>
    string? ContextMenuTarget { get; }

    /// <summary>Adds "Analyze with SpaceLens" to the right-click menu of folders, drives and folder backgrounds.</summary>
    void RegisterContextMenu(string executablePath);

    void UnregisterContextMenu();

    /// <summary>Puts SpaceLens on the desktop and in the Start menu.</summary>
    void CreateShortcuts(string executablePath);

    void RemoveShortcuts();

    /// <summary>Lists SpaceLens under Settings › Apps, with an Uninstall button that runs <c>SpaceLens.exe --uninstall</c>.</summary>
    void RegisterUninstallEntry(string executablePath, string version);

    void RemoveUninstallEntry();
}
