using System.IO;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Scanning;

namespace SpaceLens.ViewModels;

/// <summary>Turns scanned nodes into what the UI shows: icons, display names and risk.</summary>
public sealed class NodePresenter(KnownLocations locations, ICleanupRiskAnalyzer riskAnalyzer)
{
    /// <summary>"Windows (C:)" for a drive root, the folder or file name otherwise.</summary>
    public static string GetDisplayName(StorageNode node, DriveSummary? drive)
    {
        if (node is FolderNode { IsRoot: true } root && drive is not null && PathRules.IsSame(root.FullPath, drive.RootPath))
        {
            return drive.DisplayName;
        }

        return node.Name;
    }

    public RiskAssessment Assess(StorageNode node) => riskAnalyzer.Assess(node);

    public NodeIcon GetIcon(StorageNode node) => node switch
    {
        FileNode file => GetFileIcon(file),
        FolderNode { IsLink: true } => NodeIcon.Link,
        _ => GetFolderIcon(node.FullPath)
    };

    private NodeIcon GetFolderIcon(string path) => path switch
    {
        _ when string.Equals(Path.GetPathRoot(path), path, StringComparison.OrdinalIgnoreCase) => NodeIcon.Drive,
        _ when PathRules.IsSame(path, locations.UserProfile) => NodeIcon.UserFolder,
        _ when PathRules.IsSame(path, locations.Downloads) => NodeIcon.Downloads,
        _ when PathRules.IsSame(path, locations.Desktop) => NodeIcon.Desktop,
        _ when PathRules.IsSame(path, locations.Documents) => NodeIcon.Documents,
        _ when PathRules.IsSame(path, locations.Pictures) => NodeIcon.Pictures,
        _ when PathRules.IsSame(path, locations.Videos) => NodeIcon.Videos,
        _ when PathRules.IsSame(path, locations.Music) => NodeIcon.Music,
        _ => NodeIcon.Folder
    };

    private static NodeIcon GetFileIcon(FileNode file)
    {
        if (file.IsCloudOnly)
        {
            return NodeIcon.CloudFile;
        }

        return FileTypeClassifier.GetKind(file.Extension) switch
        {
            FileKind.Document or FileKind.Text => NodeIcon.Document,
            FileKind.Pdf => NodeIcon.Pdf,
            FileKind.Spreadsheet => NodeIcon.Spreadsheet,
            FileKind.Presentation => NodeIcon.Presentation,
            FileKind.Image => NodeIcon.Image,
            FileKind.Video => NodeIcon.Video,
            FileKind.Audio => NodeIcon.Audio,
            FileKind.Archive => NodeIcon.Archive,
            FileKind.DiskImage or FileKind.VirtualDisk => NodeIcon.DiskImage,
            FileKind.Installer or FileKind.Executable => NodeIcon.Application,
            FileKind.Code => NodeIcon.Code,
            FileKind.Database => NodeIcon.Database,
            FileKind.System => NodeIcon.System,
            _ => NodeIcon.File
        };
    }
}
