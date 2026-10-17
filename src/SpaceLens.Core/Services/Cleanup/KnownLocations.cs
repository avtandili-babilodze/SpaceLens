using System.Runtime.InteropServices;
using SpaceLens.Core.Interop;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>Well-known Windows folders used to judge what is safe to remove. Tests build their own instance.</summary>
public sealed record KnownLocations
{
    public required string Windows { get; init; }

    public required IReadOnlyList<string> ProgramFiles { get; init; }

    public required string ProgramData { get; init; }

    public required string UsersRoot { get; init; }

    public required string UserProfile { get; init; }

    public required string LocalAppData { get; init; }

    public required string RoamingAppData { get; init; }

    public required string Temp { get; init; }

    public required string Downloads { get; init; }

    public required string Desktop { get; init; }

    public required string Documents { get; init; }

    public required string Pictures { get; init; }

    public required string Videos { get; init; }

    public required string Music { get; init; }

    /// <summary>The user's main personal folders (never safe to remove as a whole).</summary>
    public IReadOnlyList<string> PersonalFolders => [Desktop, Documents, Pictures, Videos, Music, Downloads];

    /// <summary>Where SpaceLens keeps its own settings and history.</summary>
    public required string AppDataFolder { get; init; }

    public string AppData => Path.Join(UserProfile, "AppData");

    public static KnownLocations ForCurrentUser(string appDataFolder)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = TryGetDownloadsFolder() ?? Path.Join(profile, "Downloads");
        var programFiles = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            }
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new KnownLocations
        {
            Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            ProgramFiles = programFiles,
            ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            UsersRoot = Path.GetDirectoryName(profile) ?? profile,
            UserProfile = profile,
            LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Temp = Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
            Downloads = downloads,
            Desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            AppDataFolder = appDataFolder
        };
    }

    /// <summary>Downloads has no <see cref="Environment.SpecialFolder"/> value and can be redirected, so ask the shell.</summary>
    private static string? TryGetDownloadsFolder()
    {
        var downloadsId = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        var result = NativeMethods.SHGetKnownFolderPath(downloadsId, 0, IntPtr.Zero, out var pathPointer);
        try
        {
            return result == 0 ? Marshal.PtrToStringUni(pathPointer) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPointer);
        }
    }
}
