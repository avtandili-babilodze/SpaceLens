using System.IO;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Cleanup;

namespace SpaceLens.Tests.Support;

/// <summary>Shared fixtures: a fixed clock, known locations for a fictional user and in-memory scan trees.</summary>
public static class TestData
{
    public static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime LongAgo = Now.AddYears(-2);

    public static readonly DateTime Yesterday = Now.AddDays(-1);

    public static KnownLocations Locations { get; } = new()
    {
        Windows = @"C:\Windows",
        ProgramFiles = [@"C:\Program Files", @"C:\Program Files (x86)"],
        ProgramData = @"C:\ProgramData",
        UsersRoot = @"C:\Users",
        UserProfile = @"C:\Users\alex",
        LocalAppData = @"C:\Users\alex\AppData\Local",
        RoamingAppData = @"C:\Users\alex\AppData\Roaming",
        Temp = @"C:\Users\alex\AppData\Local\Temp",
        Downloads = @"C:\Users\alex\Downloads",
        Desktop = @"C:\Users\alex\Desktop",
        Documents = @"C:\Users\alex\Documents",
        Pictures = @"C:\Users\alex\Pictures",
        Videos = @"C:\Users\alex\Videos",
        Music = @"C:\Users\alex\Music",
        AppDataFolder = @"C:\Users\alex\AppData\Local\SpaceLens"
    };

    public static FixedClock Clock { get; } = new(Now);

    public static CleanupCatalog Catalog { get; } = new(Locations);

    public static CleanupRiskAnalyzer RiskAnalyzer { get; } = new(Catalog, Clock, new MemorySettingsStore());

    /// <summary>
    /// Builds the folder chain for <paramref name="folderPath"/> under <paramref name="root"/> and returns the last folder.
    /// </summary>
    public static FolderNode Folder(FolderNode root, string folderPath, DateTime? lastWriteUtc = null)
    {
        var relative = folderPath[root.FullPath.Length..].Trim('\\');
        var current = root;
        foreach (var segment in relative.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Folders.FirstOrDefault(f => f.Name.Equals(segment, StringComparison.OrdinalIgnoreCase))
                ?? current.AddFolder(segment, lastWriteUtc ?? LongAgo);
        }

        return current;
    }

    public static FileNode File(
        FolderNode root,
        string filePath,
        long size,
        DateTime? lastWriteUtc = null,
        FileAttributes attributes = FileAttributes.Normal)
    {
        var folder = Folder(root, Path.GetDirectoryName(filePath)!);
        return folder.AddFile(Path.GetFileName(filePath), size, lastWriteUtc ?? LongAgo, attributes);
    }

    public static ScanResult Scan(FolderNode root, DriveSummary? drive = null)
    {
        root.Aggregate();
        return new ScanResult
        {
            Root = root,
            StartedUtc = Now,
            Duration = TimeSpan.FromSeconds(1),
            IsComplete = true,
            UnreadableFolderCount = 0,
            SkippedLinkCount = 0,
            Drive = drive
        };
    }

    /// <summary>A small drive with something in every interesting category.</summary>
    public static FolderNode TypicalDrive()
    {
        var root = FolderNode.CreateRoot(@"C:\", LongAgo);
        File(root, @"C:\Windows\System32\kernel32.dll", 5 * ByteSize.MB);
        File(root, @"C:\Windows\Temp\setup.log", 2 * ByteSize.MB);
        File(root, @"C:\Program Files\App\app.exe", 50 * ByteSize.MB);
        File(root, @"C:\pagefile.sys", 4 * ByteSize.GB);
        File(root, @"C:\Users\alex\AppData\Local\Temp\old.tmp", 300 * ByteSize.MB);
        File(root, @"C:\Users\alex\AppData\Local\Temp\fresh.tmp", 100 * ByteSize.MB, Yesterday);
        File(root, @"C:\Users\alex\AppData\Local\Google\Chrome\User Data\Default\Cache\data_1", 800 * ByteSize.MB);
        File(root, @"C:\Users\alex\AppData\Local\npm-cache\_cacache\blob", 600 * ByteSize.MB);
        File(root, @"C:\Users\alex\AppData\Local\CrashDumps\app.dmp", 200 * ByteSize.MB);
        File(root, @"C:\Users\alex\AppData\Roaming\Game\saves.db", 900 * ByteSize.MB);
        File(root, @"C:\Users\alex\Downloads\setup-old.exe", 700 * ByteSize.MB);
        File(root, @"C:\Users\alex\Downloads\setup-new.msi", 400 * ByteSize.MB, Yesterday);
        File(root, @"C:\Users\alex\Downloads\photos.zip", 1200 * ByteSize.MB);
        File(root, @"C:\Users\alex\Videos\holiday.mp4", 3 * ByteSize.GB);
        File(root, @"C:\Users\alex\Documents\thesis.docx", 5 * ByteSize.MB);
        File(root, @"C:\Users\alex\Documents\vm\disk.vhdx", 20 * ByteSize.GB);
        File(root, @"C:\Users\alex\code\web\package.json", 1 * ByteSize.KB);
        File(root, @"C:\Users\alex\code\web\node_modules\react\index.js", 150 * ByteSize.MB);
        File(root, @"C:\Windows.old\Windows\explorer.exe", 9 * ByteSize.GB);
        return root;
    }
}
