namespace SpaceLens.Core.Models;

/// <summary>Every cleanup opportunity found in one scan, safest first.</summary>
public sealed class CleanupAnalysis
{
    public static readonly CleanupAnalysis Empty = new() { Opportunities = [] };

    public required IReadOnlyList<CleanupOpportunity> Opportunities { get; init; }

    public long LowRiskBytes => Opportunities.Where(o => o.Risk == CleanupRisk.Low).Sum(o => o.Bytes);

    public long ReviewBytes => Opportunities.Where(o => o.Risk == CleanupRisk.Review).Sum(o => o.Bytes);
}
