using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Scanning;

/// <summary>Maps file extensions to kinds, friendly type names and breakdown categories.</summary>
public static class FileTypeClassifier
{
    private static readonly Dictionary<string, FileKind> KindsByExtension = BuildKinds();

    private static readonly Dictionary<string, string> NamesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".exe"] = "Application",
        [".msi"] = "Windows Installer",
        [".msix"] = "App Package",
        [".msixbundle"] = "App Package",
        [".appx"] = "App Package",
        [".iso"] = "Disc Image",
        [".img"] = "Disk Image",
        [".zip"] = "Compressed (zip)",
        [".7z"] = "7-Zip Archive",
        [".rar"] = "RAR Archive",
        [".pptx"] = "PowerPoint",
        [".ppt"] = "PowerPoint",
        [".docx"] = "Word Document",
        [".doc"] = "Word Document",
        [".xlsx"] = "Excel Workbook",
        [".xls"] = "Excel Workbook",
        [".pdf"] = "PDF Document",
        [".dll"] = "Application Extension",
        [".sys"] = "System File",
        [".vhdx"] = "Virtual Disk",
        [".vhd"] = "Virtual Disk",
        [".dmp"] = "Crash Dump",
        [".log"] = "Log File",
        [".tmp"] = "Temporary File"
    };

    public static FileKind GetKind(string extension) =>
        KindsByExtension.TryGetValue(extension, out var kind) ? kind : FileKind.Other;

    public static FileCategory GetCategory(FileKind kind) => kind switch
    {
        FileKind.Video => FileCategory.Videos,
        FileKind.Image => FileCategory.Images,
        FileKind.Audio => FileCategory.Music,
        FileKind.Document or FileKind.Pdf or FileKind.Spreadsheet or FileKind.Presentation or FileKind.Text => FileCategory.Documents,
        FileKind.Archive or FileKind.DiskImage => FileCategory.Archives,
        FileKind.Installer or FileKind.Executable => FileCategory.Apps,
        FileKind.Code => FileCategory.Code,
        FileKind.System or FileKind.Database or FileKind.VirtualDisk => FileCategory.System,
        _ => FileCategory.Other
    };

    public static string GetCategoryLabel(FileCategory category) => category switch
    {
        FileCategory.Videos => "Videos",
        FileCategory.Images => "Pictures",
        FileCategory.Music => "Music",
        FileCategory.Documents => "Documents",
        FileCategory.Archives => "Archives & disk images",
        FileCategory.Apps => "Apps & installers",
        FileCategory.Code => "Code",
        FileCategory.System => "System & data files",
        _ => "Other"
    };

    public static string GetCategoryColor(FileCategory category) => category switch
    {
        FileCategory.Videos => "#9B7BF7",
        FileCategory.Images => "#38BDF8",
        FileCategory.Music => "#F472B6",
        FileCategory.Documents => "#60A5FA",
        FileCategory.Archives => "#F5C451",
        FileCategory.Apps => "#2F8CFF",
        FileCategory.Code => "#34D399",
        FileCategory.System => "#94A3B8",
        _ => "#4C668B"
    };

    public static string GetTypeName(StorageNode node)
    {
        if (node is FolderNode folder)
        {
            return folder.IsLink ? "Link (not followed)" : folder.IsUnreadable ? "Folder (no access)" : "File folder";
        }

        var file = (FileNode)node;
        if (file.IsCloudOnly)
        {
            return "Online-only file";
        }

        var extension = file.Extension;
        if (extension.Length <= 1)
        {
            return "File";
        }

        if (NamesByExtension.TryGetValue(extension, out var name))
        {
            return name;
        }

        var label = extension[1..].ToUpperInvariant();
        return GetKind(extension) switch
        {
            FileKind.Video => $"{label} Video",
            FileKind.Image => $"{label} Image",
            FileKind.Audio => $"{label} Audio",
            FileKind.Archive => $"{label} Archive",
            FileKind.Code => $"{label} Source",
            FileKind.Text => $"{label} Text",
            FileKind.Database => $"{label} Database",
            _ => $"{label} File"
        };
    }

    private static Dictionary<string, FileKind> BuildKinds()
    {
        var kinds = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
        Add(FileKind.Video, ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v", ".flv", ".mpg", ".mpeg", ".ts", ".m2ts", ".3gp");
        Add(FileKind.Image, ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".heif", ".tif", ".tiff", ".svg", ".ico");
        Add(FileKind.Image, ".raw", ".cr2", ".nef", ".arw", ".dng", ".psd");
        Add(FileKind.Audio, ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".opus", ".aiff");
        Add(FileKind.Archive, ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".cab", ".zst");
        Add(FileKind.DiskImage, ".iso", ".img", ".dmg", ".wim", ".esd");
        Add(FileKind.Installer, ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".msp");
        Add(FileKind.Executable, ".exe", ".dll", ".com", ".bat", ".cmd", ".ps1", ".jar", ".apk");
        Add(FileKind.Document, ".doc", ".docx", ".odt", ".rtf", ".pages", ".epub");
        Add(FileKind.Pdf, ".pdf");
        Add(FileKind.Spreadsheet, ".xls", ".xlsx", ".xlsm", ".ods", ".csv", ".numbers");
        Add(FileKind.Presentation, ".ppt", ".pptx", ".odp", ".key");
        Add(FileKind.Text, ".txt", ".md", ".log", ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg");
        Add(FileKind.Code, ".cs", ".xaml", ".csproj", ".sln", ".js", ".jsx", ".ts", ".tsx", ".py", ".java", ".kt", ".go", ".rs");
        Add(FileKind.Code, ".c", ".cpp", ".h", ".hpp", ".swift", ".rb", ".php", ".html", ".css", ".scss", ".sql", ".sh");
        Add(FileKind.Database, ".db", ".sqlite", ".sqlite3", ".mdf", ".ldf", ".accdb", ".mdb", ".pst", ".ost", ".kdbx", ".edb");
        Add(FileKind.VirtualDisk, ".vhd", ".vhdx", ".vmdk", ".vdi", ".qcow2", ".avhdx");
        Add(FileKind.System, ".sys", ".dmp", ".etl", ".evtx", ".cat", ".mui", ".pf", ".tmp", ".bak", ".cache", ".pak", ".bin", ".dat");
        return kinds;

        void Add(FileKind kind, params string[] extensions)
        {
            foreach (var extension in extensions)
            {
                kinds[extension] = kind;
            }
        }
    }
}
