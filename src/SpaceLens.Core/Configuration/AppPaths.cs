namespace SpaceLens.Core.Configuration;

/// <summary>Per-user storage for SpaceLens: %LOCALAPPDATA%\SpaceLens unless a test supplies its own folder.</summary>
public sealed class AppPaths
{
    public AppPaths()
        : this(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceLens"))
    {
    }

    public AppPaths(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string DatabasePath => Path.Join(Root, "scans.db");

    public string CredentialPath => Path.Join(Root, "anthropic.key.dpapi");

    public string SettingsPath => Path.Join(Root, "settings.json");

    /// <summary>Warnings and errors, including unexpected crashes. Never contains the API key.</summary>
    public string LogPath => Path.Join(Root, "logs", "spacelens.log");
}
