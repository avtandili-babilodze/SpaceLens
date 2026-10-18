using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels;

/// <summary>One card on the System space page: what Windows keeps, what removing it means, and the safe way to do it.</summary>
public sealed class SystemSpaceItemViewModel(SystemSpaceItem item)
{
    public SystemSpaceItem Item { get; } = item;

    public SystemSpaceKind Kind => Item.Kind;

    public long Bytes => Item.Bytes ?? 0;

    public string Glyph => Kind switch
    {
        SystemSpaceKind.Hibernation => "\uE708",
        SystemSpaceKind.PageFile => "\uE964",
        SystemSpaceKind.PreviousWindows => "\uE81C",
        SystemSpaceKind.UpdateDownloads => "\uE896",
        SystemSpaceKind.ComponentStore => "\uEA86",
        _ => "\uE777"
    };

    public string Title => Kind switch
    {
        SystemSpaceKind.Hibernation => "Hibernation file",
        SystemSpaceKind.PageFile => "Virtual memory (page file)",
        SystemSpaceKind.PreviousWindows => "Previous Windows installation",
        SystemSpaceKind.UpdateDownloads => "Windows Update downloads",
        SystemSpaceKind.ComponentStore => "Component store (WinSxS)",
        _ => "System Restore points"
    };

    public string SizeText => Item.Bytes switch
    {
        { } bytes when Item.Exists => ByteSize.Format(bytes),
        _ when Kind == SystemSpaceKind.RestorePoints => "Size needs admin rights",
        _ => "None"
    };

    public string Description => (Kind, Item.Exists) switch
    {
        (SystemSpaceKind.Hibernation, true) =>
            "hiberfil.sys holds a copy of your memory for Hibernate and Fast Startup. Turning hibernation off deletes it. "
            + "Your PC then can't hibernate and starts a little slower after a full shutdown.",
        (SystemSpaceKind.Hibernation, false) =>
            "Hibernation is off, so there's no hibernation file. Turn it back on if you want Hibernate or Fast Startup.",
        (SystemSpaceKind.PageFile, _) =>
            "pagefile.sys and swapfile.sys extend your memory. Windows sizes them automatically; making them smaller can make apps "
            + "crash when memory runs low, so leave them alone unless you know you need to.",
        (SystemSpaceKind.PreviousWindows, true) =>
            "Your previous version of Windows, kept for about 10 days after an upgrade so you can go back. Removing it is safe "
            + "once you're happy with the upgrade, but you can't go back afterwards.",
        (SystemSpaceKind.PreviousWindows, false) => "There's no previous Windows installation on this PC.",
        (SystemSpaceKind.UpdateDownloads, _) =>
            "Update files Windows downloaded. It deletes them after installing; Windows Update Cleanup in Temporary files "
            + "removes any leftovers.",
        (SystemSpaceKind.ComponentStore, _) =>
            "Copies of Windows components that let updates be rolled back. Most of these files are shared with Windows itself, "
            + "so the real space used is much less than shown. Cleanup removes versions replaced by newer updates.",
        _ =>
            "Snapshots that let Windows undo system changes. Windows doesn't show their size without administrator rights. "
            + "In System Protection you can delete old points or limit how much space they may use."
    };

    /// <summary>What the button does; empty when there's nothing to do.</summary>
    public string ActionText => (Kind, Item.Exists) switch
    {
        (SystemSpaceKind.Hibernation, true) => "Turn off hibernation…",
        (SystemSpaceKind.Hibernation, false) => "Turn on hibernation…",
        (SystemSpaceKind.PageFile, _) => "Open virtual memory settings",
        (SystemSpaceKind.PreviousWindows, true) => "Remove in Temporary files",
        (SystemSpaceKind.UpdateDownloads, true) => "Open Temporary files",
        (SystemSpaceKind.ComponentStore, true) => "Clean up component store…",
        (SystemSpaceKind.RestorePoints, _) => "Open System Protection",
        _ => ""
    };

    public bool HasAction => ActionText.Length > 0;

    /// <summary>Asks for administrator rights; the button says so with a shield.</summary>
    public bool NeedsAdmin => Kind is SystemSpaceKind.Hibernation or SystemSpaceKind.ComponentStore or SystemSpaceKind.RestorePoints;
}
