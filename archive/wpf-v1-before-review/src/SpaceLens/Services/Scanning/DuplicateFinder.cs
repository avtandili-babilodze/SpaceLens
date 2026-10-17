using System.IO;
using System.Security.Cryptography;
using SpaceLens.Models;
using SpaceLens.Repositories;

namespace SpaceLens.Services.Scanning;

public interface IDuplicateFinder
{
    Task<(IReadOnlyList<DuplicateGroup> Groups, int Skipped)> FindAsync(long snapshotId, IProgress<string> progress, CancellationToken token);
}

public sealed class DuplicateFinder(IScanHistoryRepository repository) : IDuplicateFinder
{
    public async Task<(IReadOnlyList<DuplicateGroup> Groups, int Skipped)> FindAsync(long snapshotId, IProgress<string> progress, CancellationToken token)
    {
        var candidates = await repository.GetDuplicateCandidatesAsync(snapshotId);
        return await Task.Run(async () =>
        {
            var groups = new List<DuplicateGroup>(); int skipped = 0, processed = 0;
            foreach (var sameSize in candidates)
            {
                var hashes = new Dictionary<string, List<FileEntry>>();
                foreach (var file in sameSize)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file.FullPath);
                        if (!info.Exists || info.Length != file.Size || info.LastWriteTimeUtc != file.ModifiedUtc || (info.Attributes & FileAttributes.ReparsePoint) != 0 || DiskScanner.HasReparseAncestor(info.Directory!)) { skipped++; continue; }
                        await using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                        info.Refresh();
                        if (info.Length != file.Size || info.LastWriteTimeUtc != file.ModifiedUtc) { skipped++; continue; }
                        if (!hashes.TryGetValue(hash, out var files)) hashes[hash] = files = [];
                        files.Add(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { skipped++; }
                    if (++processed % 10 == 0) progress.Report($"Hashed {processed:N0} candidate files · {skipped:N0} unavailable or changed");
                }
                groups.AddRange(hashes.Where(pair => pair.Value.Count > 1).Select(pair => new DuplicateGroup(pair.Key, pair.Value)));
            }
            return ((IReadOnlyList<DuplicateGroup>)groups.OrderByDescending(g => g.RedundantBytes).ToArray(), skipped);
        }, token);
    }
}
