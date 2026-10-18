using SpaceLens.Services;

namespace SpaceLens.Tests.Support;

/// <summary>A folder picker the user always cancels.</summary>
public sealed class NoFolderPicker : IFolderPicker
{
    public string? PickFolder(string title) => null;
}
