using SpaceLens.ViewModels;

namespace SpaceLens.Services;

/// <summary>Keeps the assistant conversation between sessions.</summary>
public interface IChatHistory
{
    IReadOnlyList<SavedChatMessage> Load();

    void Save(IReadOnlyList<SavedChatMessage> messages);

    void Clear();
}
