using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Settings;

public interface ISettingsStore
{
    AppSettings Current { get; }

    event EventHandler? Changed;

    void Save(AppSettings settings);
}
