namespace SpaceLens.Services;

public interface IFolderPicker
{
    /// <summary>Lets the user pick a folder to scan; null when cancelled.</summary>
    string? PickFolder();
}
