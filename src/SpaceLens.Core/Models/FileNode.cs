namespace SpaceLens.Core.Models;

public sealed class FileNode : StorageNode
{
    private const FileAttributes CloudPlaceholderAttributes =
        FileAttributes.Offline | (FileAttributes)0x00040000 | (FileAttributes)0x00400000;

    private readonly long _length;

    public FileNode(string name, FolderNode parent, long length, DateTime lastWriteUtc, FileAttributes attributes)
        : base(name, parent, lastWriteUtc)
    {
        _length = length;
        Attributes = attributes;
    }

    public FileAttributes Attributes { get; }

    /// <summary>
    /// True for OneDrive-style placeholders whose content lives in the cloud.
    /// They report a length but occupy almost no local disk space, so they count as zero.
    /// </summary>
    public bool IsCloudOnly => (Attributes & CloudPlaceholderAttributes) != 0;

    /// <summary>Length reported by the file system, even for cloud-only placeholders.</summary>
    public long Length => _length;

    public override long Size => IsCloudOnly ? 0 : _length;

    public override bool IsFolder => false;

    public string Extension => Path.GetExtension(Name);
}
