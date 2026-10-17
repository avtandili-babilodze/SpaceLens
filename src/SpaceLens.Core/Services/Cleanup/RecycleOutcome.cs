namespace SpaceLens.Core.Services.Cleanup;

/// <summary>Which paths reached the Recycle Bin, which didn't, and whether the user stopped the operation.</summary>
public sealed record RecycleOutcome(IReadOnlyList<string> Recycled, IReadOnlyList<string> Failed, bool WasCancelled);
