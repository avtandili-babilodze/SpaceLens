using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>The Storage Goal Solver.</summary>
public sealed class CleanupPlanner : ICleanupPlanner
{
    public CleanupPlan CreatePlan(CleanupAnalysis analysis, long goalBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(goalBytes);

        var steps = new List<CleanupStep>();
        var remaining = goalBytes;

        var lowRisk = analysis.Opportunities.Where(o => o.Risk == CleanupRisk.Low).OrderByDescending(o => o.Bytes);
        foreach (var opportunity in lowRisk)
        {
            if (remaining <= 0)
            {
                break;
            }

            steps.Add(new CleanupStep(opportunity, opportunity.Items, opportunity.Bytes));
            remaining -= opportunity.Bytes;
        }

        var reviewable = analysis.Opportunities.Where(o => o.Risk == CleanupRisk.Review).OrderByDescending(o => o.Bytes);
        foreach (var opportunity in reviewable)
        {
            if (remaining <= 0)
            {
                break;
            }

            var chosen = TakeUntil(opportunity.Items, remaining);
            var bytes = chosen.Sum(i => i.Size);
            steps.Add(new CleanupStep(opportunity, chosen, bytes));
            remaining -= bytes;
        }

        return new CleanupPlan(goalBytes, steps, Summarize(goalBytes, steps));
    }

    /// <summary>Largest items first, stopping as soon as the target is covered.</summary>
    private static List<StorageNode> TakeUntil(IEnumerable<StorageNode> items, long target)
    {
        var chosen = new List<StorageNode>();
        long total = 0;
        foreach (var item in items.OrderByDescending(i => i.Size))
        {
            if (total >= target)
            {
                break;
            }

            chosen.Add(item);
            total += item.Size;
        }

        return chosen;
    }

    private static string Summarize(long goalBytes, IReadOnlyList<CleanupStep> steps)
    {
        var planned = steps.Sum(s => s.Bytes);
        var lowRisk = steps.Where(s => s.Opportunity.Risk == CleanupRisk.Low).Sum(s => s.Bytes);
        var goal = ByteSize.Format(goalBytes);

        if (steps.Count == 0)
        {
            return $"SpaceLens found nothing it considers removable toward {goal}. Uninstalling apps you don't use "
                + "(Settings › Apps) or moving personal files to another drive are the remaining options.";
        }

        if (lowRisk >= goalBytes)
        {
            return $"Low-risk cleanup alone covers your {goal} goal (about {ByteSize.Format(lowRisk)}).";
        }

        if (planned >= goalBytes)
        {
            return $"About {ByteSize.Format(lowRisk)} comes from low-risk cleanup. The remaining "
                + $"{ByteSize.Format(goalBytes - lowRisk)} needs items you should review one by one.";
        }

        return $"Everything SpaceLens considers removable adds up to about {ByteSize.Format(planned)}, "
            + $"{ByteSize.Format(goalBytes - planned)} short of {goal}. Uninstalling unused apps (Settings › Apps) "
            + "or moving personal files to another drive can cover the rest.";
    }
}
