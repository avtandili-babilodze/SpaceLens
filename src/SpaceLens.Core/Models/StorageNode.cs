namespace SpaceLens.Core.Models;

/// <summary>A file or folder captured by a scan.</summary>
public abstract class StorageNode
{
    protected StorageNode(string name, FolderNode? parent, DateTime lastWriteUtc)
    {
        Name = name;
        Parent = parent;
        LastWriteUtc = lastWriteUtc;
    }

    public string Name { get; }

    public FolderNode? Parent { get; }

    public DateTime LastWriteUtc { get; }

    /// <summary>Logical size in bytes. For folders this is the recursive total.</summary>
    public abstract long Size { get; }

    public abstract bool IsFolder { get; }

    public virtual string FullPath => Parent is null ? Name : Path.Join(Parent.FullPath, Name);

    public int Depth
    {
        get
        {
            var depth = 0;
            for (var parent = Parent; parent is not null; parent = parent.Parent)
            {
                depth++;
            }

            return depth;
        }
    }

    public override string ToString() => FullPath;
}
