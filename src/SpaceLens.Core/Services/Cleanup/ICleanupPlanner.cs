using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Cleanup;

public interface ICleanupPlanner
{
    /// <summary>
    /// Builds the smallest-risk plan that reaches <paramref name="goalBytes"/>: whole low-risk groups first,
    /// then only as many reviewable items as the goal still needs. High-risk and protected items are never used.
    /// </summary>
    CleanupPlan CreatePlan(CleanupAnalysis analysis, long goalBytes);
}
