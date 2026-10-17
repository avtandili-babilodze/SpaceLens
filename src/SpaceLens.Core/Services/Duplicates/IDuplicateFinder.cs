using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Duplicates;

public interface IDuplicateFinder
{
    /// <summary>
    /// Finds files with identical contents among <paramref name="files"/>. Files are only read, never changed.
    /// Hard links (two names for the same data) are not reported as duplicates.
    /// </summary>
    Task<DuplicateSearchResult> FindAsync(
        IReadOnlyCollection<FileNode> files,
        IProgress<DuplicateProgress>? progress,
        CancellationToken cancellationToken);
}
