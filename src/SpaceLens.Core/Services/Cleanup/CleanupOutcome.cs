using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>What a recycle request achieved, in words the user can read.</summary>
public sealed record CleanupOutcome(int RecycledCount, long RecycledBytes, int FailedCount, bool WasCancelled)
{
    public static readonly CleanupOutcome Cancelled = new(0, 0, 0, WasCancelled: true);

    public string Describe()
    {
        if (WasCancelled && RecycledCount == 0)
        {
            return "Nothing was removed.";
        }

        var items = RecycledCount == 1 ? "item" : "items";
        var message = $"Moved {RecycledCount:N0} {items} ({ByteSize.Format(RecycledBytes)}) to the Recycle Bin.";
        if (FailedCount > 0)
        {
            message += $" {FailedCount:N0} couldn't be moved (in use, protected or already gone).";
        }

        return message + " Empty the Recycle Bin to free the space.";
    }
}
