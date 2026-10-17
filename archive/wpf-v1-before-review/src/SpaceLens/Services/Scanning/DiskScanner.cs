using System.Diagnostics;
using System.IO;
using SpaceLens.Models;
using SpaceLens.Repositories;
using SpaceLens.Services.Cleanup;

namespace SpaceLens.Services.Scanning;

public sealed class DiskScanner(IScanHistoryRepository repository, ICleanupRiskAnalyzer risks) : IDiskScanner
{
    public async Task<StorageSnapshot> ScanAsync(string path, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        long id = await Task.Run(() => Scan(path, progress, cancellationToken), CancellationToken.None);
        return await repository.GetSnapshotAsync(id) ?? throw new IOException("The scan snapshot could not be read.");
    }

    private long Scan(string path, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists) throw new DirectoryNotFoundException("Choose an existing accessible folder or drive.");
        if (HasReparseAncestor(rootInfo)) throw new IOException("The selected folder or one of its parents is a symbolic link or junction. Select the original physical location.");
        using var writer = repository.CreateScan(root);
        var stack = new Stack<Frame>();
        long files = 0, folders = 0, bytes = 0, errors = 0, skipped = 0;
        string current = root;
        var clock = Stopwatch.StartNew();
        ScanProgress State() => new(files, folders, bytes, errors, skipped, current);
        void Report(bool force = false) { if (force || clock.ElapsedMilliseconds >= 100) { progress?.Report(State()); clock.Restart(); } }
        FileEntry Make(FileSystemInfo info, long? parent, bool directory, bool reparse, long size = 0)
        {
            var (risk, reason) = risks.Classify(info.FullName, directory, reparse);
            string ext = directory ? "" : info.Extension.ToLowerInvariant();
            return new FileEntry { SnapshotId = writer.SnapshotId, ParentId = parent, FullPath = info.FullName, Name = info.Name.Length == 0 ? info.FullName : info.Name, IsDirectory = directory, IsReparsePoint = reparse, Size = size, FileCount = directory ? 0 : 1, ModifiedUtc = info.LastWriteTimeUtc, Extension = ext, Category = directory ? "Folders" : FileCategories.Get(ext), Risk = risk, Reason = reason, Warning = reparse ? "Reparse point skipped to avoid loops and double traversal." : null };
        }
        void Push(DirectoryInfo info, long? parent)
        {
            long id = writer.Insert(Make(info, parent, true, false));
            var frame = new Frame(id, info.FullName);
            stack.Push(frame);
            try { frame.Enumerator = info.EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false }).GetEnumerator(); }
            catch (Exception ex) when (IsAccessError(ex)) { frame.Warning = AccessMessage(ex); errors++; }
        }
        void CompleteTop()
        {
            var frame = stack.Pop(); frame.Enumerator?.Dispose();
            writer.CompleteFolder(frame.Id, frame.Size, frame.Files, frame.Folders, frame.Warning);
            if (stack.TryPeek(out var parent)) { parent.Size += frame.Size; parent.Files += frame.Files; parent.Folders += frame.Folders; }
        }
        try
        {
            Push(rootInfo, null); Report(true);
            while (stack.Count > 0 && !token.IsCancellationRequested)
            {
                var frame = stack.Peek();
                FileSystemInfo? item = null;
                try { if (frame.Enumerator?.MoveNext() == true) item = frame.Enumerator.Current; }
                catch (Exception ex) when (IsAccessError(ex)) { frame.Warning = AccessMessage(ex); errors++; }
                if (item is null) { CompleteTop(); continue; }
                current = item.FullName;
                try
                {
                    var attrs = item.Attributes;
                    bool directory = (attrs & FileAttributes.Directory) != 0;
                    bool reparse = (attrs & FileAttributes.ReparsePoint) != 0;
                    if (reparse)
                    {
                        writer.Insert(Make(item, frame.Id, directory, true)); skipped++;
                        // Links have no measured contents and are excluded from counts and byte totals.
                    }
                    else if (directory)
                    {
                        folders++; frame.Folders++; Push((DirectoryInfo)item, frame.Id);
                    }
                    else
                    {
                        long size = ((FileInfo)item).Length;
                        writer.Insert(Make(item, frame.Id, false, false, size));
                        files++; bytes += size; frame.Size += size; frame.Files++;
                    }
                }
                catch (Exception ex) when (IsAccessError(ex))
                {
                    errors++; frame.Warning = "One or more children could not be read. " + AccessMessage(ex);
                }
                Report();
            }
            if (token.IsCancellationRequested) foreach (var frame in stack) frame.Warning = "Partial data: scan cancelled before this folder was fully read.";
            while (stack.Count > 0) CompleteTop();
            writer.Finish(token.IsCancellationRequested ? ScanStatus.Cancelled : ScanStatus.Completed, State()); Report(true);
            return writer.SnapshotId;
        }
        catch
        {
            foreach (var frame in stack) frame.Enumerator?.Dispose();
            writer.Finish(ScanStatus.Failed, State()); throw;
        }
    }

    public static bool HasReparseAncestor(DirectoryInfo info)
    {
        for (DirectoryInfo? current = info; current != null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
    private static bool IsAccessError(Exception ex) => ex is UnauthorizedAccessException or IOException or System.Security.SecurityException;
    private static string AccessMessage(Exception ex) => ex is UnauthorizedAccessException or System.Security.SecurityException ? "Permission denied; inaccessible content is excluded from measured sizes." : "Some content disappeared or could not be read; measured sizes may be incomplete.";
    private sealed class Frame(long id, string path)
    {
        public long Id { get; } = id;
        public string Path { get; } = path;
        public IEnumerator<FileSystemInfo>? Enumerator { get; set; }
        public long Size, Files, Folders;
        public string? Warning { get; set; }
    }
}
