namespace SpaceLens.Core.Models;

/// <summary>The steps that reach (or come as close as possible to) a space goal.</summary>
public sealed record CleanupPlan(long GoalBytes, IReadOnlyList<CleanupStep> Steps, string Summary)
{
    public long PlannedBytes => Steps.Sum(s => s.Bytes);

    public long LowRiskBytes => Steps.Where(s => s.Opportunity.Risk == CleanupRisk.Low).Sum(s => s.Bytes);

    public bool MeetsGoal => PlannedBytes >= GoalBytes;
}
