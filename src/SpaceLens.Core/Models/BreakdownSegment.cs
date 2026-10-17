namespace SpaceLens.Core.Models;

/// <summary>One slice of a folder's storage breakdown (a child item, a file category, or "Other").</summary>
public sealed record BreakdownSegment(string Label, long Bytes, double Fraction, string Color, StorageNode? Node = null)
{
    public string SizeText => ByteSize.Format(Bytes);

    public string PercentText => Fraction >= 0.005 ? $"{Fraction * 100:0}%" : "<1%";
}
