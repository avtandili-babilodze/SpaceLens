using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels;

/// <summary>One row in a scan comparison: a folder or large file and how its size changed.</summary>
public sealed record ChangeRowViewModel(string Path, long BeforeBytes, long AfterBytes, string Note)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } name ? name : Path;

    public string Location => System.IO.Path.GetDirectoryName(Path) ?? "";

    public long Delta => AfterBytes - BeforeBytes;

    public string DeltaText => ByteSize.FormatDelta(Delta);

    public string BeforeText => BeforeBytes == 0 ? "—" : ByteSize.Format(BeforeBytes);

    public string AfterText => AfterBytes == 0 ? "—" : ByteSize.Format(AfterBytes);

    public bool IsGrowth => Delta > 0;
}
