namespace SpaceLens.Core.Services.Cleanup;

/// <summary>Everything SpaceLens removed or moved, so the user can see what happened and how much it freed.</summary>
public interface ICleanupLog
{
    /// <summary>Newest first.</summary>
    IReadOnlyList<CleanupLogEntry> Entries { get; }

    event EventHandler? Changed;

    void Add(CleanupLogEntry entry);

    void Clear();
}
