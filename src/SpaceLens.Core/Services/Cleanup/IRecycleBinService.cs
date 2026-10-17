namespace SpaceLens.Core.Services.Cleanup;

public interface IRecycleBinService
{
    /// <summary>Size of the Recycle Bin on the drive that contains <paramref name="driveRoot"/>.</summary>
    RecycleBinInfo Query(string driveRoot);

    /// <summary>
    /// Moves files and folders to the Recycle Bin so they can be restored.
    /// Windows asks before anything too large for the Recycle Bin would be deleted permanently.
    /// </summary>
    RecycleOutcome MoveToRecycleBin(IReadOnlyList<string> paths);

    /// <summary>Permanently empties the Recycle Bin on one drive. Callers must confirm with the user first.</summary>
    void Empty(string driveRoot);
}
