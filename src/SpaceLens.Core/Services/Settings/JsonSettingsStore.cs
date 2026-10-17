using System.Text.Json;
using SpaceLens.Core.Configuration;
using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Settings;

/// <summary>Non-secret preferences in a small JSON file. A missing or unreadable file falls back to defaults.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly AppPaths _paths;

    public JsonSettingsStore(AppPaths paths)
    {
        _paths = paths;
        Current = Load();
    }

    public AiSettings Current { get; private set; }

    public event EventHandler? Changed;

    public void Save(AiSettings settings)
    {
        var temporaryPath = _paths.SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, Options));
        File.Move(temporaryPath, _paths.SettingsPath, overwrite: true);
        Current = settings;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private AiSettings Load()
    {
        try
        {
            return File.Exists(_paths.SettingsPath)
                ? JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(_paths.SettingsPath)) ?? new AiSettings()
                : new AiSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AiSettings();
        }
    }
}
