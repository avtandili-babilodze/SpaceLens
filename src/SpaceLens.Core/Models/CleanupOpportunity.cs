namespace SpaceLens.Core.Models;

/// <summary>A group of items that share a reason they might be removable, such as temporary files.</summary>
public sealed class CleanupOpportunity
{
    public required CleanupKind Kind { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required CleanupRisk Risk { get; init; }

    public CleanupAction Action { get; init; } = CleanupAction.RecycleItems;

    public IReadOnlyList<StorageNode> Items { get; init; } = [];

    /// <summary>Size when it is not the sum of <see cref="Items"/>, e.g. the Recycle Bin measured by Windows.</summary>
    public long? KnownBytes { get; init; }

    public long Bytes => KnownBytes ?? Items.Sum(i => i.Size);

    public int ItemCount => Items.Count;
}
