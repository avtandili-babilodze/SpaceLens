namespace SpaceLens.Core.Models;

/// <summary>A file of 100 MB or more that is new or changed size between two snapshots.</summary>
public sealed record LargeFileChange(string Path, long BeforeBytes, long AfterBytes, DateTime? LastWriteUtc)
{
    public long Delta => AfterBytes - BeforeBytes;

    public bool IsNew => BeforeBytes == 0;
}
