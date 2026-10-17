namespace SpaceLens.Core.Models;

/// <summary>One step of a plan: an opportunity and the subset of its items chosen to reach the goal.</summary>
public sealed record CleanupStep(CleanupOpportunity Opportunity, IReadOnlyList<StorageNode> Items, long Bytes)
{
    public bool IsPartial => Opportunity.Action == CleanupAction.RecycleItems && Items.Count < Opportunity.ItemCount;
}
