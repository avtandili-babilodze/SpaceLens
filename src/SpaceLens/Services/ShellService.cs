using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace SpaceLens.Services;

public sealed class ShellService : IShellService
{
    public void RevealInExplorer(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new InvalidOperationException("This item no longer exists on disk.");
        }

        var explorer = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var start = new ProcessStartInfo(explorer) { UseShellExecute = false };

        // ArgumentList quotes the path, so names with spaces or commas can't change the command.
        start.ArgumentList.Add("/select," + path);
        Run(() => Process.Start(start), "File Explorer couldn't be opened.");
    }

    public void CopyToClipboard(string text) =>
        Run(() => Clipboard.SetText(text), "Another app is using the clipboard. Try again in a moment.");

    public void OpenStorageSettings() => OpenUrl("ms-settings:storagesense");

    public void OpenUrl(string url) =>
        Run(() => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }), "Windows couldn't open " + url + ".");

    /// <summary>Process and clipboard failures surface as Win32 or COM errors; both derive from ExternalException.</summary>
    private static void Run(Action action, string failureMessage)
    {
        try
        {
            action();
        }
        catch (ExternalException exception)
        {
            throw new InvalidOperationException(failureMessage, exception);
        }
    }
}
