using System.IO;

namespace SpaceLens.Infrastructure;

public sealed class AppPaths
{
    public string Root { get; }
    public string DatabasePath => Path.Combine(Root, "scans.db");
    public string CredentialPath => Path.Combine(Root, "anthropic.dpapi");
    public AppPaths() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceLens")) { }
    public AppPaths(string root) { Root = Path.GetFullPath(root); Directory.CreateDirectory(Root); }
}
