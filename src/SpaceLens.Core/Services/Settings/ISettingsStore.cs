using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Settings;

public interface ISettingsStore
{
    AiSettings Current { get; }

    event EventHandler? Changed;

    void Save(AiSettings settings);
}
