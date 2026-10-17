using System.ComponentModel;
using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Session;

/// <summary>
/// The scan the user is looking at, plus everything derived from it. Property changes are raised for
/// <see cref="Current"/>, <see cref="Cleanup"/>, <see cref="Growth"/>, <see cref="IsAnalyzing"/> and <see cref="HistoryError"/>.
/// </summary>
public interface IScanSession : INotifyPropertyChanged
{
    ScanResult? Current { get; }

    CleanupAnalysis Cleanup { get; }

    /// <summary>Changes since the previous scan of the same location, when there is one.</summary>
    SnapshotComparison? Growth { get; }

    bool IsAnalyzing { get; }

    /// <summary>Why the scan couldn't be saved to history, if that failed.</summary>
    string? HistoryError { get; }

    bool HasScan { get; }

    /// <summary>A different scan was loaded.</summary>
    event EventHandler? ScanLoaded;

    /// <summary>Items were removed from the current scan, so sizes and analyses changed.</summary>
    event EventHandler? ContentsChanged;

    /// <summary>Makes <paramref name="result"/> the current scan, analyses it and records it in history.</summary>
    Task LoadAsync(ScanResult result);

    /// <summary>Runs a read-only query over the current tree on a background thread, never during a removal.</summary>
    Task<T> ReadAsync<T>(Func<ScanResult, T> read, CancellationToken cancellationToken = default);

    /// <summary>Detaches items that no longer exist on disk and refreshes everything derived from the tree.</summary>
    Task RemoveAsync(IReadOnlyCollection<StorageNode> nodes);

    Task RefreshCleanupAsync();
}
