namespace SpaceLens.Core.Services.Cleanup;

/// <summary>
/// One cleanup action. <see cref="Summary"/> names the largest items; <see cref="Location"/> is the drive
/// that was cleaned, or the destination folder for a move.
/// </summary>
public sealed record CleanupLogEntry(
    DateTime TimeUtc,
    CleanupLogAction Action,
    int ItemCount,
    long Bytes,
    string Summary,
    string Location)
{
    /// <summary>
    /// Space this action cleared out of the user's folders. Emptying the Recycle Bin isn't counted again,
    /// because the items in it were already counted when they were moved there.
    /// </summary>
    public long ClearedBytes => Action == CleanupLogAction.EmptiedRecycleBin ? 0 : Bytes;
}
