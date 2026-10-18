using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Settings;

namespace SpaceLens.Tests.Support;

public sealed class MemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; private set; } = new() { ShareScanDetails = true };

    public event EventHandler? Changed;

    public void Save(AppSettings settings)
    {
        Current = settings;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
