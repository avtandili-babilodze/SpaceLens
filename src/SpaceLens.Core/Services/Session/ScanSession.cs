using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Repositories;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Storage;

namespace SpaceLens.Core.Services.Session;

/// <summary>
/// Holds the current scan and what is derived from it. Background readers and removals share one gate,
/// so a recycle never changes the tree under a running query.
/// </summary>
public sealed class ScanSession(
    ICleanupAnalyzer cleanupAnalyzer,
    IStorageHistoryService history,
    ILogger<ScanSession> logger) : IScanSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ScanResult? _current;
    private CleanupAnalysis _cleanup = CleanupAnalysis.Empty;
    private SnapshotComparison? _growth;
    private bool _isAnalyzing;
    private string? _historyError;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ScanLoaded;

    public event EventHandler? ContentsChanged;

    public ScanResult? Current
    {
        get => _current;
        private set => SetField(ref _current, value, nameof(HasScan));
    }

    public CleanupAnalysis Cleanup
    {
        get => _cleanup;
        private set => SetField(ref _cleanup, value);
    }

    public SnapshotComparison? Growth
    {
        get => _growth;
        private set => SetField(ref _growth, value);
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set => SetField(ref _isAnalyzing, value);
    }

    public string? HistoryError
    {
        get => _historyError;
        private set => SetField(ref _historyError, value);
    }

    public bool HasScan => Current is not null;

    public async Task LoadAsync(ScanResult result)
    {
        Current = result;
        Cleanup = CleanupAnalysis.Empty;
        Growth = null;
        HistoryError = null;
        ScanLoaded?.Invoke(this, EventArgs.Empty);

        await RefreshCleanupAsync();
        if (result.IsComplete)
        {
            await RecordHistoryAsync(result);
        }
    }

    public async Task<T> ReadAsync<T>(Func<ScanResult, T> read, CancellationToken cancellationToken = default)
    {
        var scan = Current ?? throw new InvalidOperationException("Scan a drive or folder first.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => read(scan), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(IReadOnlyCollection<StorageNode> nodes)
    {
        if (Current is null || nodes.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            // Remove the deepest items first so a folder and something inside it can both be listed.
            foreach (var node in nodes.OrderByDescending(n => n.Depth))
            {
                node.Parent?.Remove(node);
            }
        }
        finally
        {
            _gate.Release();
        }

        ContentsChanged?.Invoke(this, EventArgs.Empty);
        await RefreshCleanupAsync();
    }

    public async Task RefreshCleanupAsync()
    {
        var scan = Current;
        if (scan is null)
        {
            return;
        }

        IsAnalyzing = true;
        try
        {
            var analysis = await ReadAsync(s => cleanupAnalyzer.Analyze(s));
            if (ReferenceEquals(Current, scan))
            {
                Cleanup = analysis;
            }
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private async Task RecordHistoryAsync(ScanResult result)
    {
        try
        {
            // The record is built under the gate, so a recycle right after the scan can't change the tree mid-read.
            var record = await ReadAsync(history.CreateRecord);
            var growth = await history.SaveAsync(record);
            if (ReferenceEquals(Current, result))
            {
                Growth = growth;
            }
        }
        catch (Exception exception) when (exception is HistoryStoreException or InvalidOperationException)
        {
            // History is a bonus feature: a locked or corrupt database must not break the scan the user just ran.
            logger.LogWarning(exception, "Couldn't save scan of {Root} to history", result.RootPath);
            HistoryError = "Scan history couldn't be saved: " + exception.Message;
        }
    }

    private void SetField<T>(ref T field, T value, string? alsoChanged = null, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (alsoChanged is not null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(alsoChanged));
        }
    }
}
