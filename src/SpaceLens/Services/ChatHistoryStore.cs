using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SpaceLens.Core.Configuration;
using SpaceLens.ViewModels;

namespace SpaceLens.Services;

/// <summary>
/// The latest <see cref="MaxMessages"/> chat messages in a JSON file next to the settings. Only what the panel shows
/// is kept (no scan details beyond the paths Claude already named); a damaged file starts an empty chat.
/// </summary>
public sealed class ChatHistoryStore(AppPaths paths, ILogger<ChatHistoryStore> logger) : IChatHistory
{
    public const int MaxMessages = 100;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public IReadOnlyList<SavedChatMessage> Load()
    {
        try
        {
            return File.Exists(paths.ChatPath)
                ? JsonSerializer.Deserialize<List<SavedChatMessage>>(File.ReadAllText(paths.ChatPath), Options) ?? []
                : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Couldn't read the saved chat; starting an empty one");
            return [];
        }
    }

    public void Save(IReadOnlyList<SavedChatMessage> messages)
    {
        try
        {
            var latest = messages.Skip(Math.Max(0, messages.Count - MaxMessages)).ToList();
            var temporaryPath = paths.ChatPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(latest, Options));
            File.Move(temporaryPath, paths.ChatPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Couldn't save the chat");
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(paths.ChatPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Couldn't delete the saved chat");
        }
    }
}
