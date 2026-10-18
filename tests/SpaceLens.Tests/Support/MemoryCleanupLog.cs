using SpaceLens.Core.Services.Cleanup;

namespace SpaceLens.Tests.Support;

public sealed class MemoryCleanupLog : ICleanupLog
{
    private readonly List<CleanupLogEntry> _entries = [];

    public IReadOnlyList<CleanupLogEntry> Entries => _entries;

    public event EventHandler? Changed;

    public void Add(CleanupLogEntry entry)
    {
        _entries.Insert(0, entry);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _entries.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
