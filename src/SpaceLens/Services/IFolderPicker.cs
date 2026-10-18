namespace SpaceLens.Services;

public interface IFolderPicker
{
    /// <summary>Lets the user pick a folder; null when cancelled.</summary>
    string? PickFolder(string title);
}
