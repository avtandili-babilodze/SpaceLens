using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Cleanup;

public interface ICleanupAnalyzer
{
    /// <summary>Groups the scan's removable items into opportunities, safest first. Never includes high-risk or protected items.</summary>
    CleanupAnalysis Analyze(ScanResult scan, CancellationToken cancellationToken = default);
}
