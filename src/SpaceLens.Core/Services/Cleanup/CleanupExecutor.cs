using Microsoft.Extensions.Logging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Interaction;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Session;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>
/// The only code path that removes or moves anything. It re-checks every item's risk, never touches protected or
/// high-risk items, and either uses the Recycle Bin (so the user can undo) or moves items to another drive.
/// Every action is written to the cleanup log.
/// </summary>
public sealed class CleanupExecutor(
    ICleanupRiskAnalyzer riskAnalyzer,
    IRecycleBinService recycleBin,
    IFileMover mover,
    IDriveService drives,
    IConfirmationService confirmation,
    IScanSession session,
    ICleanupLog log,
    TimeProvider clock,
    ILogger<CleanupExecutor> logger) : ICleanupExecutor
{
    private const int DetailLines = 6;
    private const int SummaryNames = 3;

    private const string NothingRemovable =
        "The selected items are protected, high risk, or contain data that can't be recreated, so SpaceLens won't remove them.";

    private const string NothingMovable =
        "The selected items are protected or high risk (Windows, installed programs, app data), so SpaceLens won't move them.";

    public async Task<CleanupOutcome> RecycleAsync(IReadOnlyList<StorageNode> items)
    {
        var allowed = Allowed(items);
        var blockedByRisk = items.Count - allowed.Count;

        // A folder that looks disposable can still hold a repository or a database; keep those folders out.
        var hidingData = allowed
            .OfType<FolderNode>()
            .Where(folder => riskAnalyzer.FindIrreplaceableContents(folder, limit: 1).Count > 0)
            .ToList();
        var removable = WithoutNested(allowed.Except(hidingData).ToList());

        if (removable.Count == 0)
        {
            confirmation.Inform("Nothing to remove", NothingRemovable);
            return CleanupOutcome.Cancelled;
        }

        if (!confirmation.Confirm(BuildRecycleRequest(removable, blockedByRisk, hidingData.Count)))
        {
            return CleanupOutcome.Cancelled;
        }

        var byPath = removable.ToDictionary(i => i.FullPath, StringComparer.OrdinalIgnoreCase);
        var outcome = await Task.Run(() => recycleBin.MoveToRecycleBin(byPath.Keys.ToList()));
        var recycled = outcome.Recycled.Select(path => byPath[path]).ToList();
        var recycledBytes = recycled.Sum(i => i.Size);

        await session.RemoveAsync(recycled);
        Record(CleanupLogAction.MovedToRecycleBin, recycled, recycledBytes, DriveOf(removable[0].FullPath));
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

        var drive = DriveOf(scan.RootPath);
        var confirmed = confirmation.Confirm(new ConfirmationRequest(
            $"Empty the Recycle Bin on {drive}?",
            $"This permanently deletes {ByteSize.Format(knownBytes)} of items you already deleted. It can't be undone.",
            "Empty Recycle Bin",
            Footnote: "Open the Recycle Bin on your desktop first if you want to restore anything.",
            IsDestructive: true));
        if (!confirmed)
        {
            return false;
        }

        await Task.Run(() => recycleBin.Empty(drive + "\\"));
        log.Add(new CleanupLogEntry(Now, CleanupLogAction.EmptiedRecycleBin, 0, knownBytes, "Recycle Bin", drive));
        logger.LogInformation("Emptied the Recycle Bin on {Drive}", drive);
        await session.RefreshCleanupAsync();
        return true;
    }

    public async Task<MoveOutcome> MoveAsync(IReadOnlyList<StorageNode> items, string destinationFolder)
    {
        // The same risk rules as recycling, except that folders holding repositories or databases may move:
        // moving loses nothing.
        var allowed = Allowed(items);
        var movable = WithoutNested(allowed);
        if (movable.Count == 0)
        {
            confirmation.Inform("Nothing to move", NothingMovable);
            return MoveOutcome.Cancelled;
        }

        var sourceDrive = DriveOf(movable[0].FullPath);
        var destinationDrive = DriveOf(destinationFolder);
        if (string.Equals(sourceDrive, destinationDrive, StringComparison.OrdinalIgnoreCase))
        {
            confirmation.Inform(
                "Choose another drive",
                $"Moving items within {sourceDrive} doesn't free any space on it. Pick a folder on another drive, such as an external disk.");
            return MoveOutcome.Cancelled;
        }

        var bytes = movable.Sum(i => i.Size);
        if (drives.TryGetDrive(destinationFolder) is { } target && target.FreeBytes < bytes)
        {
            confirmation.Inform(
                "Not enough space there",
                $"{target.DisplayName} has {ByteSize.Format(target.FreeBytes)} free, but these items need {ByteSize.Format(bytes)}.");
            return MoveOutcome.Cancelled;
        }

        if (!confirmation.Confirm(BuildMoveRequest(movable, items.Count - allowed.Count, destinationFolder, sourceDrive)))
        {
            return MoveOutcome.Cancelled;
        }

        var byPath = movable.ToDictionary(i => i.FullPath, StringComparer.OrdinalIgnoreCase);
        var result = await RunOnStaThreadAsync(() => mover.Move(byPath.Keys.ToList(), destinationFolder));
        var moved = result.Moved.Select(path => byPath[path]).ToList();
        var movedBytes = moved.Sum(i => i.Size);

        await session.RemoveAsync(moved);
        Record(CleanupLogAction.MovedToFolder, moved, movedBytes, destinationFolder);
        logger.LogInformation(
            "Moved {Moved} of {Requested} items ({Bytes} bytes) to another drive; {Failed} failed, cancelled: {Cancelled}",
            moved.Count,
            movable.Count,
            movedBytes,
            result.Failed.Count,
            result.WasCancelled);
        return new MoveOutcome(moved.Count, movedBytes, result.Failed.Count, result.WasCancelled, destinationFolder);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>"C:" for any path on drive C.</summary>
    private static string DriveOf(string path) => (Path.GetPathRoot(path) ?? path).TrimEnd('\\');

    /// <summary>The shell's progress and conflict windows need a thread with a message loop that COM can use.</summary>
    private static Task<T> RunOnStaThreadAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "SpaceLens file move"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
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

    /// <summary>Moving or recycling a folder takes everything inside it, so selected items within it are redundant.</summary>
    private static List<StorageNode> WithoutNested(List<StorageNode> items)
    {
        var selectedFolders = new HashSet<FolderNode>(items.OfType<FolderNode>(), ReferenceEqualityComparer.Instance);
        return items.Where(item => !HasAncestorIn(item, selectedFolders)).ToList();
    }

    private static bool IsInsideRecycleBin(StorageNode item) =>
        item.FullPath.Contains(@"\$Recycle.Bin", StringComparison.OrdinalIgnoreCase);

    private static string Summarize(List<StorageNode> items)
    {
        var names = items.OrderByDescending(i => i.Size).Take(SummaryNames).Select(i => i.Name).ToList();
        var more = items.Count - names.Count;
        return more > 0 ? $"{string.Join(", ", names)} and {more:N0} more" : string.Join(", ", names);
    }

    private List<StorageNode> Allowed(IReadOnlyList<StorageNode> items) =>
        items.Where(item => riskAnalyzer.Assess(item).AllowsRemoval && !IsInsideRecycleBin(item)).ToList();

    private void Record(CleanupLogAction action, List<StorageNode> items, long bytes, string location)
    {
        if (items.Count > 0)
        {
            log.Add(new CleanupLogEntry(Now, action, items.Count, bytes, Summarize(items), location));
        }
    }

    private List<string> Details(List<StorageNode> items)
    {
        var details = items
            .OrderByDescending(i => i.Size)
            .Take(DetailLines)
            .Select(i => $"{i.Name}  ·  {ByteSize.Format(i.Size)}  ·  {riskAnalyzer.Assess(i).Label}")
            .ToList();
        if (items.Count > DetailLines)
        {
            details.Add($"…and {items.Count - DetailLines:N0} more");
        }

        return details;
    }

    private ConfirmationRequest BuildRecycleRequest(List<StorageNode> removable, int blockedByRisk, int hidingData)
    {
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
            Details(removable),
            footnote,
            IsDestructive: true);
    }

    private ConfirmationRequest BuildMoveRequest(List<StorageNode> movable, int blockedByRisk, string destination, string sourceDrive)
    {
        var footnote = "Apps and shortcuts that use these files won't find them in the old place. "
            + "Windows shows its progress window and asks before replacing anything in the destination.";
        if (blockedByRisk > 0)
        {
            footnote = $"{blockedByRisk:N0} protected or high-risk item(s) were left out. " + footnote;
        }

        var items = movable.Count == 1 ? "item" : "items";
        return new ConfirmationRequest(
            $"Move {movable.Count:N0} {items} to {destination}?",
            $"{ByteSize.Format(movable.Sum(i => i.Size))} will move off {sourceDrive}, freeing that space there.",
            "Move",
            Details(movable),
            footnote);
    }
}
