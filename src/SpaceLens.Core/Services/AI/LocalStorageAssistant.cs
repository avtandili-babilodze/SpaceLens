using System.Text;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Session;

namespace SpaceLens.Core.Services.AI;

/// <summary>
/// Answers common questions from the scan without any network access. It recognises a handful of intents
/// (free X GB, what's largest, is this safe, what grew) and replies with facts computed on the PC.
/// </summary>
public sealed class LocalStorageAssistant(
    IScanSession session,
    ICleanupRiskAnalyzer riskAnalyzer,
    ICleanupPlanner planner) : ILocalStorageAssistant
{
    private static readonly string[] GoalWords = ["free", "clean", "recover", "make room", "reclaim", "need"];
    private static readonly string[] GrowthWords = ["grow", "grew", "since", "changed", "increase", "bigger"];
    private static readonly string[] SafetyWords = ["safe", "delete", "remove", "get rid"];
    private static readonly string[] CleanupWords = ["unnecessary", "junk", "clean", "free", "waste", "temp", "cache"];
    private static readonly string[] WholeScanWords = ["drive", "disk", "computer", " pc", "full", "overall", "everything"];

    public Task<AiReply> AskAsync(string question, StorageNode? focus, CancellationToken cancellationToken)
    {
        if (session.Current is not { } scan)
        {
            return Task.FromResult(new AiReply("Scan a drive or folder first, and I'll answer from what's actually on it."));
        }

        var reply = AnswerAsync(scan, question, focus, cancellationToken);
        return scan.IsComplete ? reply : WithPartialScanNoteAsync(reply);
    }

    private static async Task<AiReply> WithPartialScanNoteAsync(Task<AiReply> reply)
    {
        var answer = await reply;
        const string PartialNote = "This scan was stopped before it finished, so these numbers only cover what was read.";
        return answer with { Text = PartialNote + "\n\n" + answer.Text };
    }

    private Task<AiReply> AnswerAsync(ScanResult scan, string question, StorageNode? focus, CancellationToken cancellationToken)
    {
        var text = question.ToLowerInvariant();
        if (ContainsAny(text, GoalWords) && StorageGoalParser.TryParse(question, out var goal))
        {
            return Task.FromResult(PlanFor(goal));
        }

        if (ContainsAny(text, GrowthWords))
        {
            return Task.FromResult(DescribeGrowth());
        }

        if (ContainsAny(text, SafetyWords) && focus is not null && !ReferenceEquals(focus, scan.Root))
        {
            return Task.FromResult(DescribeSafety(focus));
        }

        if (ContainsAny(text, CleanupWords))
        {
            return Task.FromResult(DescribeOpportunities());
        }

        var asksAboutWholeScan = ContainsAny(text, WholeScanWords);
        return session.ReadAsync(
            current => DescribeLargest(current, asksAboutWholeScan ? current.Root : focus as FolderNode ?? focus?.Parent ?? current.Root),
            cancellationToken);
    }

    private AiReply PlanFor(long goal)
    {
        var plan = planner.CreatePlan(session.Cleanup, goal);
        var intro = plan.Steps.Count == 0
            ? plan.Summary
            : $"Here {DescribeWays(plan.Steps.Count)} to free about {ByteSize.Format(plan.PlannedBytes)}. {plan.Summary}";
        return new AiReply(intro, plan);
    }

    private AiReply DescribeGrowth()
    {
        var growth = session.Growth;
        if (growth is null)
        {
            return new AiReply(
                "I can only see growth once this location has been scanned at least twice. "
                + "Scan again later, or compare older scans on the Compare page.");
        }

        var text = new StringBuilder();
        var since = growth.Before.CreatedUtc.ToLocalTime();
        text.AppendLine($"Since {since:MMM d}, this location changed by **{ByteSize.FormatDelta(growth.NetChange)}**.");
        foreach (var change in growth.Grew.Take(5))
        {
            text.AppendLine($"- [[{change.Path}]] {ByteSize.FormatDelta(change.Delta)}{(change.IsNew ? " (new)" : "")}");
        }

        text.Append("These are measured differences between two scans; they don't show which app made the change.");
        return new AiReply(text.ToString());
    }

    private AiReply DescribeSafety(StorageNode node)
    {
        var risk = riskAnalyzer.Assess(node);
        return new AiReply($"[[{node.FullPath}]] is **{risk.Label}** ({ByteSize.Format(node.Size)}). {risk.Reason}");
    }

    private AiReply DescribeOpportunities()
    {
        var cleanup = session.Cleanup;
        if (cleanup.Opportunities.Count == 0)
        {
            return new AiReply(
                "I didn't find caches, temporary files or other leftovers worth cleaning in this scan. "
                + "The largest folders are your best lead.");
        }

        var text = new StringBuilder();
        var lowRisk = ByteSize.Format(cleanup.LowRiskBytes);
        text.AppendLine($"About **{lowRisk}** is low-risk to clean, plus {ByteSize.Format(cleanup.ReviewBytes)} worth reviewing:");
        foreach (var opportunity in cleanup.Opportunities.Take(6))
        {
            text.AppendLine($"- **{opportunity.Title}**: {ByteSize.Format(opportunity.Bytes)} ({opportunity.Risk.ToLabel()})");
        }

        text.Append("Open Cleanup to review each group before anything is moved to the Recycle Bin.");
        return new AiReply(text.ToString());
    }

    private AiReply DescribeLargest(ScanResult scan, FolderNode folder)
    {
        var text = new StringBuilder();
        var size = ByteSize.Format(folder.Size);
        text.AppendLine($"[[{folder.FullPath}]] holds **{size}** in {folder.FileCount:N0} files. The biggest items:");
        foreach (var child in folder.Children.OrderByDescending(c => c.Size).Take(5))
        {
            text.AppendLine($"- [[{child.FullPath}]] {ByteSize.Format(child.Size)}");
        }

        var largestFile = StorageAnalyzer.LargestFiles(folder, 1).FirstOrDefault();
        if (largestFile is not null)
        {
            text.AppendLine($"The largest single file is [[{largestFile.FullPath}]] ({ByteSize.Format(largestFile.Size)}).");
        }

        if (scan.UnreadableFolderCount > 0)
        {
            text.Append($"{scan.UnreadableFolderCount:N0} folders couldn't be read, so their contents aren't counted.");
        }

        return new AiReply(text.ToString().TrimEnd());
    }

    private static bool ContainsAny(string text, string[] words) => words.Any(text.Contains);

    private static string DescribeWays(int count) => count == 1 ? "is 1 way" : $"are {count} ways";
}
