namespace SpaceLens.Core.Services.Apps;

/// <summary>Splits an app's UninstallString into the program to start and its arguments.</summary>
public static class UninstallCommandLine
{
    /// <summary>
    /// Handles quoted paths ("C:\App\uninstall.exe" /S), unquoted paths with spaces (C:\Program Files\App\unins000.exe),
    /// environment variables, and Windows Installer entries, whose /I (change) switch becomes /X (remove).
    /// </summary>
    public static (string FileName, string Arguments) Split(string commandLine)
    {
        var text = Environment.ExpandEnvironmentVariables(commandLine.Trim());
        string fileName;
        string arguments;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            fileName = end > 0 ? text[1..end] : text.Trim('"');
            arguments = end > 0 ? text[(end + 1)..].Trim() : "";
        }
        else
        {
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            var split = exe >= 0 ? exe + 4 : text.IndexOf(' ');
            fileName = split > 0 && split < text.Length ? text[..split] : text;
            arguments = split > 0 && split < text.Length ? text[split..].Trim() : "";
        }

        if (Path.GetFileName(fileName).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            arguments = arguments.Replace("/I{", "/X{", StringComparison.OrdinalIgnoreCase)
                .Replace("/I {", "/X {", StringComparison.OrdinalIgnoreCase);
        }

        return (fileName, arguments);
    }
}
