namespace SpaceLens.Core.Models;

/// <summary>The largest matches of a name search, and how many matched in total.</summary>
public sealed record SearchResult(IReadOnlyList<StorageNode> Items, int TotalMatches)
{
    public bool IsTruncated => TotalMatches > Items.Count;
}
