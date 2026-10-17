namespace SpaceLens.Core.Models;

/// <summary>How much a folder's size changed between two snapshots.</summary>
public sealed record FolderChange(string Path, long BeforeBytes, long AfterBytes)
{
    public long Delta => AfterBytes - BeforeBytes;

    /// <summary>Not present (or under the 1 MB recording threshold) in the earlier snapshot.</summary>
    public bool IsNew => BeforeBytes == 0;

    public bool IsGone => AfterBytes == 0;
}
