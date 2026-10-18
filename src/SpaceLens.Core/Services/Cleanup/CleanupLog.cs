using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SpaceLens.Core.Configuration;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>
/// The cleanup log in a small JSON file, newest first. Only the latest <see cref="MaxEntries"/> are kept.
/// A missing or unreadable file starts an empty log; failing to write it never fails the cleanup itself.
/// </summary>
public sealed class CleanupLog : ICleanupLog
{
    public const int MaxEntries = 500;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;
    private readonly ILogger<CleanupLog> _logger;
    private readonly Lock _lock = new();
    private List<CleanupLogEntry> _entries;

    public CleanupLog(AppPaths paths, ILogger<CleanupLog> logger)
    {
        _paths = paths;
        _logger = logger;
        _entries = Load();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<CleanupLogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public void Add(CleanupLogEntry entry)
    {
        lock (_lock)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            }

            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries = [];
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<CleanupLogEntry> Load()
    {
        try
        {
            return File.Exists(_paths.CleanupLogPath)
                ? JsonSerializer.Deserialize<List<CleanupLogEntry>>(File.ReadAllText(_paths.CleanupLogPath), Options) ?? []
                : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Couldn't read the cleanup log; starting a new one");
            return [];
        }
    }

    private void Save()
    {
        try
        {
            var temporaryPath = _paths.CleanupLogPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_entries, Options));
            File.Move(temporaryPath, _paths.CleanupLogPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Couldn't save the cleanup log");
        }
    }
}
