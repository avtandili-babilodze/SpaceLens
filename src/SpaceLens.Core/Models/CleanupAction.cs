namespace SpaceLens.Core.Models;

/// <summary>How the space of a cleanup opportunity is reclaimed.</summary>
public enum CleanupAction
{
    /// <summary>The user reviews the items and moves the ones they choose to the Recycle Bin.</summary>
    RecycleItems,

    /// <summary>Space is already in the Recycle Bin; emptying it frees the space.</summary>
    EmptyRecycleBin,

    /// <summary>Only Windows can remove it safely (Windows.old, update caches); open Storage settings.</summary>
    OpenStorageSettings
}
