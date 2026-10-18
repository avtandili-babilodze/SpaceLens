using System.Windows;
using SpaceLens.ViewModels;

namespace SpaceLens.Infrastructure;

/// <summary>
/// Optional command-line switches, mainly for demos and automated UI checks:
/// <c>--scan &lt;path&gt;</c> scans on launch, <c>--page &lt;name&gt;</c> opens a page,
/// <c>--select &lt;path&gt;</c> opens an item in the explorer, <c>--ask &lt;question&gt;</c> asks the assistant,
/// <c>--capture &lt;file.png&gt;</c> saves a screenshot of the window and exits, and <c>--welcome</c> greets a fresh install.
/// "Analyze with SpaceLens" in File Explorer's right-click menu starts SpaceLens with <c>--scan</c>.
/// </summary>
public sealed record StartupOptions(
    string? ScanPath,
    AppPage? Page,
    string? SelectPath,
    string? Question,
    string? CapturePath,
    bool Welcome = false)
{
    // Lets layout, bindings and background analysis settle before a screenshot is taken.
    private static readonly TimeSpan CaptureDelay = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan AnswerPollInterval = TimeSpan.FromMilliseconds(200);

    public static StartupOptions Parse(IReadOnlyList<string> args) => new(
        CleanPath(Value(args, "--scan")),
        Enum.TryParse<AppPage>(Value(args, "--page"), ignoreCase: true, out var page) ? page : null,
        Value(args, "--select"),
        Value(args, "--ask"),
        Value(args, "--capture"),
        args.Contains("--welcome", StringComparer.OrdinalIgnoreCase));

    public async Task ApplyAsync(MainViewModel viewModel, Window window)
    {
        if (Welcome)
        {
            viewModel.Notifications.Show("SpaceLens is installed. Open it from the desktop or the Start menu, or right-click any folder › Analyze with SpaceLens.");
        }

        if (ScanPath is not null)
        {
            await viewModel.Scan.ScanPathAsync(ScanPath, CancellationToken.None);
        }

        if (SelectPath is not null && viewModel.Explorer.Tree.Find(SelectPath) is { } node)
        {
            viewModel.Explorer.NavigateTo(node);
        }

        if (Page is { } page)
        {
            viewModel.NavigateCommand.Execute(page);
        }

        if (Question is not null)
        {
            viewModel.Assistant.SendCommand.Execute(Question);
            while (viewModel.Assistant.IsBusy)
            {
                await Task.Delay(AnswerPollInterval);
            }
        }

        if (CapturePath is not null)
        {
            await Task.Delay(CaptureDelay);
            WindowCapture.SaveAsPng(window, CapturePath);
            Application.Current.Shutdown();
        }
    }

    /// <summary>
    /// Explorer passes a drive as "C:\", and the closing quote after a backslash arrives as part of the argument
    /// (C:"), so the quote is dropped and the backslash put back.
    /// </summary>
    private static string? CleanPath(string? path)
    {
        var cleaned = path?.Trim().TrimEnd('"').Trim();
        if (string.IsNullOrEmpty(cleaned))
        {
            return null;
        }

        return cleaned.Length == 2 && cleaned[1] == ':' ? cleaned + "\\" : cleaned;
    }

    private static string? Value(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
