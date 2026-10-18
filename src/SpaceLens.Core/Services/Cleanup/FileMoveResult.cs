namespace SpaceLens.Core.Services.Cleanup;

/// <summary>Which paths left their original place, and which are still there.</summary>
public sealed record FileMoveResult(IReadOnlyList<string> Moved, IReadOnlyList<string> Failed, bool WasCancelled);
