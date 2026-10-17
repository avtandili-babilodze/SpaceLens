namespace SpaceLens.Core.Models;

/// <summary>All duplicate groups found, plus how many files were looked at and skipped.</summary>
public sealed record DuplicateSearchResult(IReadOnlyList<DuplicateGroup> Groups, int CandidateCount, int SkippedCount)
{
    public long RedundantBytes => Groups.Sum(g => g.RedundantBytes);
}
