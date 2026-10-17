namespace SpaceLens.Core.Models;

/// <summary>Files whose contents are byte-for-byte identical (same size and SHA-256).</summary>
public sealed record DuplicateGroup(string Hash, long FileSize, IReadOnlyList<FileNode> Files)
{
    /// <summary>Space that could be recovered by keeping one copy.</summary>
    public long RedundantBytes => FileSize * Math.Max(0, Files.Count - 1);
}
