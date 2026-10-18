namespace SpaceLens.Core.Models;

public enum SystemSpaceKind
{
    /// <summary>hiberfil.sys: memory saved for Hibernate and Fast Startup.</summary>
    Hibernation,

    /// <summary>pagefile.sys and swapfile.sys: virtual memory.</summary>
    PageFile,

    /// <summary>Windows.old and the upgrade folders: the previous Windows installation.</summary>
    PreviousWindows,

    /// <summary>Downloaded Windows updates waiting to be installed or already installed.</summary>
    UpdateDownloads,

    /// <summary>WinSxS: every component version Windows keeps so updates can be rolled back.</summary>
    ComponentStore,

    /// <summary>System Restore points (shadow copies), stored in System Volume Information.</summary>
    RestorePoints
}
