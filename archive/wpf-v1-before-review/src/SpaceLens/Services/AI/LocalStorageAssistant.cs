using System.Text;
using SpaceLens.Models;
using SpaceLens.Repositories;
using SpaceLens.Services.Cleanup;

namespace SpaceLens.Services.AI;

public sealed class LocalStorageAssistant(IScanHistoryRepository repository, ICleanupPlanner planner)
{
    public async Task<AiReply> AskAsync(string question, StorageSnapshot snapshot, FileEntry selected, SnapshotComparison? comparison)
    {
        if (snapshot.Status != ScanStatus.Completed) return new("This scan is incomplete. Finish a new scan before drawing storage or cleanup conclusions.", []);
        var text = new StringBuilder();
        var refs = new List<AiReference>();
        if (question.Contains("grew", StringComparison.OrdinalIgnoreCase) || question.Contains("growth", StringComparison.OrdinalIgnoreCase) || question.Contains("since", StringComparison.OrdinalIgnoreCase) || question.Contains("growing", StringComparison.OrdinalIgnoreCase))
        {
            if (comparison is null) return new("Compare two completed scans of the same location on Compare Scans first. I cannot infer growth from a single snapshot.", []);
            text.AppendLine($"Measured change: {comparison.DeltaText} between {comparison.Before.CreatedUtc.ToLocalTime():g} and {comparison.After.CreatedUtc.ToLocalTime():g}.");
            foreach (var c in comparison.Changes.Where(c => !c.IsDirectory).Take(8)) text.AppendLine($"• {c.FullPath}: {c.DeltaText} ({c.Kind})");
            text.Append("These are observed size differences; I cannot establish which application caused them. Access errors and files changed during a scan can affect the comparison.");
            return new(text.ToString(), []);
        }
        if (question.Contains("free", StringComparison.OrdinalIgnoreCase) || question.Contains("clean", StringComparison.OrdinalIgnoreCase))
        {
            long goal;
            try { goal = StorageGoalParser.Parse(question); } catch (FormatException) { goal = 10L * 1024 * 1024 * 1024; }
            var plan = await planner.CreatePlanAsync(snapshot, goal);
            text.AppendLine($"Goal: {ByteSize.Format(goal)}. Potential review pool: {plan.PotentialText} across {plan.Candidates.Count} files.");
            text.AppendLine(plan.Explanation);
            foreach (var file in plan.Candidates.Take(5)) { text.AppendLine($"• {file.Name} · {file.SizeText} · {file.RiskText}"); refs.Add(new(file.Id, file.Name, file.FullPath)); }
            return new(text.ToString(), refs);
        }
        if (question.Contains("safe", StringComparison.OrdinalIgnoreCase) || question.Contains("delete", StringComparison.OrdinalIgnoreCase))
        {
            return new($"{selected.Name} · {selected.RiskText}\n{selected.Reason}\n\nNo deletion is performed by SpaceLens. File size cannot establish whether data is unnecessary. Review backups and use the owning application's cleanup controls for application data.", [new(selected.Id, selected.Name, selected.FullPath)]);
        }
        string term = question.Contains("AppData", StringComparison.OrdinalIgnoreCase) ? "AppData" : question.Contains("cache", StringComparison.OrdinalIgnoreCase) ? "cache" : "";
        var root = await repository.GetRootAsync(snapshot.Id) ?? selected;
        var page = await repository.GetChildrenAsync(new(snapshot.Id, term.Length > 0 ? root.Id : selected.Id, Search: term, Filter: term.Length > 0 ? "Folders" : question.Contains("file", StringComparison.OrdinalIgnoreCase) ? "Files" : "All items", Limit: 10, Recursive: term.Length > 0 || question.Contains("file", StringComparison.OrdinalIgnoreCase)));
        long? threshold = null;
        if (question.Contains("larger", StringComparison.OrdinalIgnoreCase) || question.Contains("over", StringComparison.OrdinalIgnoreCase))
            try { threshold = StorageGoalParser.Parse(question); } catch (FormatException) { }
        var matches = page.Entries.Where(e => threshold is null || e.Size > threshold).Take(8).ToArray();
        text.AppendLine($"{selected.FullPath} contains {selected.SizeText} of measured logical file data ({selected.FileCount:N0} files). Largest matching items:");
        if (matches.Length == 0) text.AppendLine("No matching measured entries were found in this scope.");
        foreach (var e in matches) { text.AppendLine($"• {e.Name} · {e.SizeText} · {e.RiskText}"); refs.Add(new(e.Id, e.Name, e.FullPath)); }
        text.Append(term.Length > 0 ? "Names suggest a category, but do not establish ownership or whether content is disposable. Inspect the folder and use its application's supported cleanup controls." : "Folder sizes are recursive. This offline answer uses measured metadata and does not infer that large files are unnecessary.");
        if (snapshot.Errors > 0) text.Append($" {snapshot.Errors:N0} access errors excluded unreadable content.");
        return new(text.ToString(), refs);
    }
}
