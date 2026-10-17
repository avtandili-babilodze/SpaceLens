namespace SpaceLens.Services;

/// <summary>
/// Hands things over to Windows: File Explorer, the clipboard, Settings and the browser.
/// Every method throws <see cref="InvalidOperationException"/> with a message for the user when Windows can't do it.
/// </summary>
public interface IShellService
{
    /// <summary>Opens File Explorer with the item selected.</summary>
    void RevealInExplorer(string path);

    void CopyToClipboard(string text);

    /// <summary>Opens Settings › System › Storage, where Windows cleans its own leftovers.</summary>
    void OpenStorageSettings();

    void OpenUrl(string url);
}
