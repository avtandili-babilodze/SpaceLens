using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>What a move request achieved, in words the user can read.</summary>
public sealed record MoveOutcome(int MovedCount, long MovedBytes, int FailedCount, bool WasCancelled, string Destination)
{
    public static readonly MoveOutcome Cancelled = new(0, 0, 0, WasCancelled: true, "");

    public string Describe()
    {
        if (MovedCount == 0)
        {
            return FailedCount > 0 ? $"Nothing was moved; {FailedCount:N0} item(s) couldn't be moved." : "Nothing was moved.";
        }

        var items = MovedCount == 1 ? "item" : "items";
        var message = $"Moved {MovedCount:N0} {items} ({ByteSize.Format(MovedBytes)}) to {Destination}.";
        if (FailedCount > 0)
        {
            message += $" {FailedCount:N0} couldn't be moved (in use, protected or skipped).";
        }

        return message;
    }
}
