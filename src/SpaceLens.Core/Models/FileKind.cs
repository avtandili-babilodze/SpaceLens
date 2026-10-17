namespace SpaceLens.Core.Models;

/// <summary>What a file is, judged from its extension.</summary>
public enum FileKind
{
    Other,
    Document,
    Pdf,
    Spreadsheet,
    Presentation,
    Text,
    Image,
    Video,
    Audio,
    Archive,
    DiskImage,
    Installer,
    Executable,
    Code,
    Database,
    VirtualDisk,
    System
}
