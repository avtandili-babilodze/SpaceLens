using Microsoft.Extensions.Logging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Interaction;
using SpaceLens.Core.Services.Session;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>
/// The only code path that removes anything. It re-checks every item's risk, never touches protected or
/// high-risk items (or folders hiding them), and always uses the Recycle Bin so the user can undo.
/// </summary>
public sealed class CleanupExecutor(
    ICleanupRiskAnalyzer riskAnalyzer,
    IRecycleBinService recycleBin,
    IConfirmationService confirmation,
    IScanSession session,
    ILogger<CleanupExecutor> logger) : ICleanupExecutor
{
    private const int DetailLines = 6;

    private const string NothingRemovable =
        "The selected items are protected, high risk, or contain data that can't be recreated, so SpaceLens won't remove them.";

    public async Task<CleanupOutcome> RecycleAsync(IReadOnlyList<StorageNode> items)
    {
        var allowed = items.Where(item => riskAnalyzer.Assess(item).AllowsRemoval && !IsInsideRecycleBin(item)).ToList();
        var blockedByRisk = items.Count - allowed.Count;

        // A folder that looks disposable can still hold a repository or a database; keep those folders out.
        var hidingData = allowed
            .OfType<FolderNode>()
            .Where(folder => riskAnalyzer.FindIrreplaceableContents(folder, limit: 1).Count > 0)
            .ToList();
        allowed = allowed.Except(hidingData).ToList();

        // Recycling a folder takes everything inside it, so selected items within it are redundant.
        var selectedFolders = new HashSet<FolderNode>(allowed.OfType<FolderNode>(), ReferenceEqualityComparer.Instance);
        var removable = allowed.Where(item => !HasAncestorIn(item, selectedFolders)).ToList();

        if (removable.Count == 0)
        {
            confirmation.Inform("Nothing to remove", NothingRemovable);
            return CleanupOutcome.Cancelled;
        }

        if (!confirmation.Confirm(BuildRequest(removable, blockedByRisk, hidingData.Count)))
        {
            return CleanupOutcome.Cancelled;
        }

        var byPath = removable.ToDictionary(i => i.FullPath, StringComparer.OrdinalIgnoreCase);
        var outcome = await Task.Run(() => recycleBin.MoveToRecycleBin(byPath.Keys.ToList()));
        var recycled = outcome.Recycled.Select(path => byPath[path]).ToList();
        var recycledBytes = recycled.Sum(i => i.Size);

        await session.RemoveAsync(recycled);
        logger.LogInformation(
            "Recycled {Recycled} of {Requested} items ({Bytes} bytes); {Failed} failed, cancelled: {Cancelled}",
            recycled.Count,
            removable.Count,
            recycledBytes,
            outcome.Failed.Count,
            outcome.WasCancelled);
        return new CleanupOutcome(recycled.Count, recycledBytes, outcome.Failed.Count, outcome.WasCancelled);
    }

    public async Task<bool> EmptyRecycleBinAsync(long knownBytes)
    {
        var scan = session.Current;
        if (scan is null)
        {
            return false;
        }

        var drive = Path.GetPathRoot(scan.RootPath) ?? scan.RootPath;
        var confirmed = confirmation.Confirm(new ConfirmationRequest(
            $"Empty the Recycle Bin on {drive.TrimEnd('\\')}?",
            $"This permanently deletes {ByteSize.Format(knownBytes)} of items you already deleted. It can't be undone.",
            "Empty Recycle Bin",
            Footnote: "Open the Recycle Bin on your desktop first if you want to restore anything.",
            IsDestructive: true));
        if (!confirmed)
        {
            return false;
        }

        await Task.Run(() => recycleBin.Empty(drive));
        logger.LogInformation("Emptied the Recycle Bin on {Drive}", drive);
        await session.RefreshCleanupAsync();
        return true;
    }

    private static bool HasAncestorIn(StorageNode item, HashSet<FolderNode> folders)
    {
        for (var parent = item.Parent; parent is not null; parent = parent.Parent)
        {
            if (folders.Contains(parent))
            {
                return true;
            }
        }

        return false;
    }

    private ConfirmationRequest BuildRequest(List<StorageNode> removable, int blockedByRisk, int hidingData)
    {
        var details = removable
            .OrderByDescending(i => i.Size)
            .Take(DetailLines)
            .Select(i => $"{i.Name}  ·  {ByteSize.Format(i.Size)}  ·  {riskAnalyzer.Assess(i).Label}")
            .ToList();
        if (removable.Count > DetailLines)
        {
            details.Add($"…and {removable.Count - DetailLines:N0} more");
        }

        var footnote = "You can restore everything from the Recycle Bin. The space is freed once the Recycle Bin is emptied.";
        if (hidingData > 0)
        {
            var leftOut = $"{hidingData:N0} folder(s) were left out because they contain repositories, databases or virtual disks.";
            footnote = leftOut + " " + footnote;
        }

        if (blockedByRisk > 0)
        {
            footnote = $"{blockedByRisk:N0} protected or high-risk item(s) were left out. " + footnote;
        }

        var items = removable.Count == 1 ? "item" : "items";
        return new ConfirmationRequest(
            $"Move {removable.Count:N0} {items} to the Recycle Bin?",
            $"{ByteSize.Format(removable.Sum(i => i.Size))} will be moved to the Recycle Bin.",
            "Move to Recycle Bin",
            details,
            footnote,
            IsDestructive: true);
    }

    private static bool IsInsideRecycleBin(StorageNode item) =>
        item.FullPath.Contains(@"\$Recycle.Bin", StringComparison.OrdinalIgnoreCase);
}
