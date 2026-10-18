using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Cleanup;

namespace SpaceLens.ViewModels;

/// <summary>One row of the cleanup history.</summary>
public sealed class HistoryEntryViewModel(CleanupLogEntry entry)
{
    public CleanupLogEntry Entry { get; } = entry;

    public string WhenText => Entry.TimeUtc.ToLocalTime().ToString("MMM d, yyyy  ·  HH:mm");

    public string Glyph => Entry.Action switch
    {
        CleanupLogAction.MovedToFolder => "\uE8DE",
        CleanupLogAction.EmptiedRecycleBin => "\uE74D",
        _ => "\uE74D"
    };

    public string Title => Entry.Action switch
    {
        CleanupLogAction.MovedToFolder => $"Moved {Items} to {Entry.Location}",
        CleanupLogAction.EmptiedRecycleBin => $"Emptied the Recycle Bin on {Entry.Location}",
        _ => $"Moved {Items} on {Entry.Location} to the Recycle Bin"
    };

    public string Summary => Entry.Summary;

    public string SizeText => ByteSize.Format(Entry.Bytes);

    private string Items => Entry.ItemCount == 1 ? "1 item" : $"{Entry.ItemCount:N0} items";
}
