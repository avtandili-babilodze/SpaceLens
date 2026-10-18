using SpaceLens.Services;
using SpaceLens.ViewModels;

namespace SpaceLens.Tests.Support;

public sealed class NoChatHistory : IChatHistory
{
    public IReadOnlyList<SavedChatMessage> Load() => [];

    public void Save(IReadOnlyList<SavedChatMessage> messages)
    {
    }

    public void Clear()
    {
    }
}
