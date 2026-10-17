namespace SpaceLens.Core.Models;

/// <summary>How careful the user must be before removing an item. Ordered from safest to most dangerous.</summary>
public enum CleanupRisk
{
    Low,
    Review,
    High,
    Protected
}
