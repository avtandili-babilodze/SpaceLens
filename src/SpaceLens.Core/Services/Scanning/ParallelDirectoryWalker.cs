using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Runtime.ExceptionServices;
using System.Security;
using SpaceLens.Core.Interop;
using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Scanning;

/// <summary>
/// Lists a directory tree with several workers in parallel, adding every folder and file to the tree under
/// <paramref name="root"/>. Each folder is listed exactly once; links and junctions are recorded but never followed.
/// </summary>
internal sealed class ParallelDirectoryWalker(FolderNode root, CancellationToken cancellationToken)
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(120);

    // Files are counted locally and published in batches to keep contention between workers low.
    private const int CounterFlushInterval = 4096;

    // LIFO keeps the walk depth-first, so the queue stays small even on very wide drives.
    private readonly BlockingCollection<(FolderNode Folder, string Path)> _queue = new(new ConcurrentStack<(FolderNode, string)>());

    // Folders queued or being read. The walk is finished when it drops to zero.
    private int _pending;
    private long _files;
    private long _folders;
    private long _bytes;
    private long _unreadable;
    private long _links;
    private string _currentPath = root.FullPath;
    private Exception? _failure;

    public long UnreadableFolders => Interlocked.Read(ref _unreadable);

    public long Links => Interlocked.Read(ref _links);

    /// <summary>Walks the whole tree, reporting progress from the calling thread. Returns early when cancelled.</summary>
    public void Run(IProgress<ScanProgress>? progress)
    {
        _pending = 1;
        _queue.Add((root, root.FullPath));

        var workerCount = Math.Clamp(Environment.ProcessorCount, 2, 8);
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Factory.StartNew(Work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
            .ToArray();

        while (!Task.WaitAll(workers, ProgressInterval))
        {
            progress?.Report(Snapshot());
        }

        progress?.Report(Snapshot());
        _queue.Dispose();

        if (_failure is not null)
        {
            ExceptionDispatchInfo.Throw(_failure);
        }
    }

    private ScanProgress Snapshot() => new(
        Interlocked.Read(ref _files),
        Interlocked.Read(ref _folders),
        Interlocked.Read(ref _bytes),
        Interlocked.Read(ref _unreadable),
        Volatile.Read(ref _currentPath));

    private void Work()
    {
        try
        {
            foreach (var (folder, path) in _queue.GetConsumingEnumerable(cancellationToken))
            {
                try
                {
                    ReadFolder(folder, path);
                }
                catch (Exception exception) when (IsAccessProblem(exception))
                {
                    folder.MarkUnreadable();
                    Interlocked.Increment(ref _unreadable);
                }
                catch (Exception exception)
                {
                    // Anything else is a bug or a broken environment: stop every worker and report it.
                    Interlocked.CompareExchange(ref _failure, exception, null);
                    _queue.CompleteAdding();
                }
                finally
                {
                    if (Interlocked.Decrement(ref _pending) == 0)
                    {
                        _queue.CompleteAdding();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation leaves the remaining folders unscanned; the result is marked partial.
        }
    }

    private void ReadFolder(FolderNode folder, string path)
    {
        Volatile.Write(ref _currentPath, path);

        using var entries = new DirectoryEntries(path);
        long files = 0;
        long bytes = 0;
        while (entries.MoveNext())
        {
            var entry = entries.Current;
            if (entry.IsDirectory)
            {
                AddSubfolder(folder, path, entry);
                continue;
            }

            var file = folder.AddFile(entry.Name, entry.Length, entry.LastWriteUtc, entry.Attributes);
            files++;
            bytes += file.Size;
            if (files % CounterFlushInterval == 0)
            {
                Interlocked.Add(ref _files, CounterFlushInterval);
                Interlocked.Add(ref _bytes, bytes);
                bytes = 0;
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        Interlocked.Add(ref _files, files % CounterFlushInterval);
        Interlocked.Add(ref _bytes, bytes);

        if (entries.HasFailed)
        {
            folder.MarkUnreadable();
            Interlocked.Increment(ref _unreadable);
        }
    }

    private void AddSubfolder(FolderNode parent, string parentPath, DirectoryEntry entry)
    {
        var childPath = Path.Join(parentPath, entry.Name);
        var isLink = entry.Attributes.HasFlag(FileAttributes.ReparsePoint) && ReparsePoints.IsLink(childPath);
        var child = parent.AddFolder(entry.Name, entry.LastWriteUtc, isLink);
        Interlocked.Increment(ref _folders);

        if (isLink)
        {
            Interlocked.Increment(ref _links);
            return;
        }

        // Count the folder before queueing it, so the walk can't look finished while it waits.
        Interlocked.Increment(ref _pending);
        _queue.Add((child, childPath));
    }

    private static bool IsAccessProblem(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or SecurityException;

    private readonly record struct DirectoryEntry(
        string Name,
        FileAttributes Attributes,
        long Length,
        bool IsDirectory,
        DateTime LastWriteUtc);

    /// <summary>Lists one directory level. Errors are recorded instead of thrown so one bad folder never stops a scan.</summary>
    private sealed class DirectoryEntries(string path) : FileSystemEnumerator<DirectoryEntry>(path, Options)
    {
        private static readonly EnumerationOptions Options = new()
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
            BufferSize = 32 * 1024
        };

        public bool HasFailed { get; private set; }

        protected override DirectoryEntry TransformEntry(ref FileSystemEntry entry) => new(
            entry.FileName.ToString(),
            entry.Attributes,
            entry.IsDirectory ? 0 : entry.Length,
            entry.IsDirectory,
            entry.LastWriteTimeUtc.UtcDateTime);

        protected override bool ContinueOnError(int error)
        {
            HasFailed = true;
            return true;
        }
    }
}
