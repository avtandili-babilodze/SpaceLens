using SpaceLens.Services;

namespace SpaceLens.Tests.Support;

/// <summary>A shell that does nothing, so view model tests never open Explorer or touch the clipboard.</summary>
public sealed class NoShell : IShellService
{
    public void RevealInExplorer(string path)
    {
    }

    public void CopyToClipboard(string text)
    {
    }

    public void OpenStorageSettings()
    {
    }

    public void OpenUrl(string url)
    {
    }
}
