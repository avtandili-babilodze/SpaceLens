using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using SpaceLens.Core.Interop;
using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Duplicates;

/// <summary>Same size, then a fingerprint of the first and last 64 KB, then a full SHA-256.</summary>
public sealed class DuplicateFinder : IDuplicateFinder
{
    /// <summary>Files smaller than this rarely matter for disk space and would make the search slow.</summary>
    public const long MinimumSize = 100 * ByteSize.KB;

    private const int SampleSize = 64 * 1024;

    public Task<DuplicateSearchResult> FindAsync(
        IReadOnlyCollection<FileNode> files,
        IProgress<DuplicateProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => Find(files, progress, cancellationToken), cancellationToken);

    private static DuplicateSearchResult Find(
        IReadOnlyCollection<FileNode> files,
        IProgress<DuplicateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sameSize = files
            .Where(f => f.Size >= MinimumSize && !f.IsCloudOnly)
            .GroupBy(f => f.Size)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Key)
            .ToList();

        var total = sameSize.Sum(g => g.Count());
        var hashed = 0;
        var skipped = 0;
        var groups = new List<DuplicateGroup>();

        foreach (var group in sameSize)
        {
            // A cheap fingerprint of the first and last 64 KB rules out most same-size files before a full read.
            var bySample = new Dictionary<string, List<(FileNode File, string Path)>>();
            var seenFiles = new HashSet<(uint Volume, ulong Index)>();
            foreach (var file in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = file.FullPath;
                var sample = TrySample(path, file.Size, seenFiles);
                hashed++;
                if (sample is null)
                {
                    skipped++;
                }
                else
                {
                    bySample.TryAdd(sample, []);
                    bySample[sample].Add((file, path));
                }

                progress?.Report(new DuplicateProgress(hashed, total, skipped));
            }

            foreach (var candidates in bySample.Values.Where(c => c.Count > 1))
            {
                groups.AddRange(ConfirmByFullHash(group.Key, candidates, ref skipped, cancellationToken));
            }
        }

        return new DuplicateSearchResult(groups.OrderByDescending(g => g.RedundantBytes).ToList(), total, skipped);
    }

    private static IEnumerable<DuplicateGroup> ConfirmByFullHash(
        long size,
        List<(FileNode File, string Path)> candidates,
        ref int skipped,
        CancellationToken cancellationToken)
    {
        // Files of at most two samples were already read completely.
        if (size <= SampleSize * 2)
        {
            return [new DuplicateGroup("", size, candidates.Select(c => c.File).ToList())];
        }

        var byHash = new Dictionary<string, List<FileNode>>();
        foreach (var (file, path) in candidates)
        {
            var hash = TryHash(path, cancellationToken);
            if (hash is null)
            {
                skipped++;
                continue;
            }

            byHash.TryAdd(hash, []);
            byHash[hash].Add(file);
        }

        return byHash.Where(h => h.Value.Count > 1).Select(h => new DuplicateGroup(h.Key, size, h.Value)).ToList();
    }

    private static string? TrySample(string path, long size, HashSet<(uint, ulong)> seenFiles)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (RandomAccess.GetLength(handle) != size || !seenFiles.Add(GetFileId(handle)))
            {
                // Changed since the scan, or a hard link to a file already in this group.
                return null;
            }

            var buffer = new byte[(int)Math.Min(size, SampleSize * 2)];
            if (size <= SampleSize * 2)
            {
                ReadExactly(handle, buffer, 0);
            }
            else
            {
                ReadExactly(handle, buffer.AsSpan(0, SampleSize), 0);
                ReadExactly(handle, buffer.AsSpan(SampleSize), size - SampleSize);
            }

            return Convert.ToHexString(SHA256.HashData(buffer));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void ReadExactly(SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        while (!buffer.IsEmpty)
        {
            var read = RandomAccess.Read(handle, buffer, offset);
            if (read == 0)
            {
                throw new IOException("The file ended earlier than expected.");
            }

            buffer = buffer[read..];
            offset += read;
        }
    }

    private static string? TryHash(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1024 * 1024,
                FileOptions.SequentialScan);
            using var sha = SHA256.Create();
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sha.TransformBlock(buffer, 0, read, null, 0);
            }

            sha.TransformFinalBlock([], 0, 0);
            return Convert.ToHexString(sha.Hash!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (uint Volume, ulong Index) GetFileId(SafeFileHandle handle)
    {
        if (!NativeMethods.GetFileInformationByHandle(handle, out var info))
        {
            throw new IOException("Couldn't read the file's identity.");
        }

        return (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }
}
