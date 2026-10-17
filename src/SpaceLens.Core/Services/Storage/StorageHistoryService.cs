using SpaceLens.Core.Models;
using SpaceLens.Core.Repositories;
using SpaceLens.Core.Services.Cleanup;

namespace SpaceLens.Core.Services.Storage;

/// <summary>Saves completed scans and works out what changed between two of them.</summary>
public sealed class StorageHistoryService(IScanHistoryRepository repository) : IStorageHistoryService
{
    /// <summary>Folders smaller than this are not stored; they never explain meaningful growth.</summary>
    public const long FolderThreshold = ByteSize.MB;

    public const long LargeFileThreshold = 100 * ByteSize.MB;

    private const int SnapshotsKeptPerLocation = 12;
    private const string SnapshotMissing = "That scan is no longer in history.";
    private const int ChangesShown = 15;

    // A folder whose growth is at least this share of its parent's growth replaces the parent in the list.
    private const double ExplainedShare = 0.8;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => repository.InitializeAsync(cancellationToken);

    public SnapshotRecord CreateRecord(ScanResult scan)
    {
        if (!scan.IsComplete)
        {
            throw new InvalidOperationException("Only completed scans are saved to history.");
        }

        var folders = new List<SnapshotFolderRow> { new(scan.Root.FullPath, scan.Root.Size, scan.Root.FileCount) };
        var files = new List<SnapshotFileRow>();
        foreach (var node in scan.Root.Descendants())
        {
            switch (node)
            {
                case FolderNode folder when folder.Size >= FolderThreshold:
                    folders.Add(new SnapshotFolderRow(folder.FullPath, folder.Size, folder.FileCount));
                    break;
                case FileNode file when file.Size >= LargeFileThreshold:
                    files.Add(new SnapshotFileRow(file.FullPath, file.Size, file.LastWriteUtc));
                    break;
            }
        }

        return new SnapshotRecord(
            scan.RootPath,
            scan.CompletedUtc,
            scan.Root.Size,
            scan.Root.FileCount,
            scan.Root.FolderCount,
            scan.UnreadableFolderCount,
            folders,
            files);
    }

    public async Task<SnapshotComparison?> SaveAsync(SnapshotRecord record, CancellationToken cancellationToken = default)
    {
        var saved = await repository.SaveAsync(record, cancellationToken);
        await repository.PruneAsync(record.RootPath, SnapshotsKeptPerLocation, cancellationToken);

        var previous = await repository.GetPreviousAsync(record.RootPath, saved.Id, cancellationToken);
        return previous is null ? null : await CompareAsync(previous, saved, cancellationToken);
    }

    public Task<IReadOnlyList<StorageSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default) =>
        repository.GetSnapshotsAsync(cancellationToken);

    public async Task<SnapshotComparison> CompareAsync(long beforeId, long afterId, CancellationToken cancellationToken = default)
    {
        var first = await repository.GetSnapshotAsync(beforeId, cancellationToken)
            ?? throw new InvalidOperationException(SnapshotMissing);
        var second = await repository.GetSnapshotAsync(afterId, cancellationToken)
            ?? throw new InvalidOperationException(SnapshotMissing);
        if (first.Id == second.Id || !PathRules.IsSame(first.RootPath, second.RootPath))
        {
            throw new InvalidOperationException("Pick two different scans of the same location.");
        }

        // Always compare older to newer, whichever order the user picked them in.
        return first.Id < second.Id
            ? await CompareAsync(first, second, cancellationToken)
            : await CompareAsync(second, first, cancellationToken);
    }

    public Task DeleteAllAsync(CancellationToken cancellationToken = default) => repository.DeleteAllAsync(cancellationToken);

    /// <summary>
    /// Keeps the most specific folders: when C:\Users grew by 10 GB because C:\Users\me\Videos and
    /// C:\Users\me\Downloads grew by 6 GB and 4 GB, only those two folders are listed.
    /// </summary>
    internal static List<FolderChange> MostSpecific(IEnumerable<FolderChange> changes, bool growth)
    {
        var candidates = changes
            .Where(c => growth ? c.Delta > 0 : c.Delta < 0)
            .OrderByDescending(c => Math.Abs(c.Delta))
            .Take(400);

        // Deepest first, so a parent is judged against the children that are already listed.
        var kept = new List<FolderChange>();
        foreach (var change in candidates.OrderByDescending(c => c.Path.Count(ch => ch == '\\')))
        {
            var inside = kept.Where(k => PathRules.IsInside(k.Path, change.Path)).ToList();
            var outermost = inside.Where(k => !inside.Any(other => !ReferenceEquals(other, k) && PathRules.IsInside(k.Path, other.Path)));
            var explained = outermost.Sum(k => Math.Abs(k.Delta));
            if (explained < Math.Abs(change.Delta) * ExplainedShare)
            {
                kept.Add(change);
            }
        }

        return kept.OrderByDescending(c => Math.Abs(c.Delta)).Take(ChangesShown).ToList();
    }

    private async Task<SnapshotComparison> CompareAsync(StorageSnapshot before, StorageSnapshot after, CancellationToken cancellationToken)
    {
        var folders = await repository.GetFolderChangesAsync(before.Id, after.Id, cancellationToken);
        var files = await repository.GetLargeFileChangesAsync(before.Id, after.Id, cancellationToken);

        return new SnapshotComparison(
            before,
            after,
            MostSpecific(folders, growth: true),
            MostSpecific(folders, growth: false),
            files.Where(f => f.Delta > 0).Take(ChangesShown).ToList());
    }
}
