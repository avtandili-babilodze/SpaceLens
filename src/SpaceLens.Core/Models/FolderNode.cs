namespace SpaceLens.Core.Models;

public sealed class FolderNode : StorageNode
{
    private readonly List<FolderNode> _folders = [];
    private readonly List<FileNode> _files = [];
    private readonly string? _rootPath;
    private string? _fullPath;
    private long _size;
    private long _fileCount;
    private long _folderCount;

    private FolderNode(string name, FolderNode? parent, DateTime lastWriteUtc, string? rootPath, bool isLink)
        : base(name, parent, lastWriteUtc)
    {
        _rootPath = rootPath;
        IsLink = isLink;
    }

    public IReadOnlyList<FolderNode> Folders => _folders;

    public IReadOnlyList<FileNode> Files => _files;

    public IEnumerable<StorageNode> Children => _folders.Concat<StorageNode>(_files);

    public int ChildCount => _folders.Count + _files.Count;

    public override long Size => _size;

    public override bool IsFolder => true;

    /// <summary>Number of files anywhere below this folder.</summary>
    public long FileCount => _fileCount;

    /// <summary>Number of folders anywhere below this folder.</summary>
    public long FolderCount => _folderCount;

    /// <summary>A junction or symbolic link. Its target is never traversed, so it has no measured contents.</summary>
    public bool IsLink { get; }

    /// <summary>The scanner could not list this folder (usually access denied).</summary>
    public bool IsUnreadable { get; private set; }

    public bool IsRoot => Parent is null;

    public override string FullPath => _fullPath ??= _rootPath ?? Path.Join(Parent!.FullPath, Name);

    public static FolderNode CreateRoot(string fullPath, DateTime lastWriteUtc)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(fullPath);
        var name = Path.GetFileName(trimmed);
        return new FolderNode(string.IsNullOrEmpty(name) ? fullPath : name, null, lastWriteUtc, fullPath, isLink: false);
    }

    /// <summary>Adds a child folder. Only one thread may add children to a given folder.</summary>
    public FolderNode AddFolder(string name, DateTime lastWriteUtc, bool isLink = false)
    {
        var folder = new FolderNode(name, this, lastWriteUtc, rootPath: null, isLink);
        _folders.Add(folder);
        return folder;
    }

    /// <summary>Adds a child file. Only one thread may add children to a given folder.</summary>
    public FileNode AddFile(string name, long length, DateTime lastWriteUtc, FileAttributes attributes = FileAttributes.Normal)
    {
        var file = new FileNode(name, this, length, lastWriteUtc, attributes);
        _files.Add(file);
        return file;
    }

    public void MarkUnreadable() => IsUnreadable = true;

    /// <summary>
    /// Recomputes the recursive totals of this subtree after it was built.
    /// Runs iteratively so very deep trees cannot overflow the stack.
    /// </summary>
    public void Aggregate()
    {
        var pending = new Stack<(FolderNode Folder, bool ChildrenDone)>();
        pending.Push((this, false));

        while (pending.Count > 0)
        {
            var (folder, childrenDone) = pending.Pop();
            if (!childrenDone)
            {
                pending.Push((folder, true));
                foreach (var child in folder._folders)
                {
                    pending.Push((child, false));
                }

                continue;
            }

            long size = 0;
            long files = folder._files.Count;
            long folders = folder._folders.Count;
            foreach (var file in folder._files)
            {
                size += file.Size;
            }

            foreach (var child in folder._folders)
            {
                size += child._size;
                files += child._fileCount;
                folders += child._folderCount;
            }

            folder._size = size;
            folder._fileCount = files;
            folder._folderCount = folders;
        }
    }

    /// <summary>Detaches a direct child (for example after it was recycled) and updates every ancestor's totals.</summary>
    public bool Remove(StorageNode child)
    {
        var removed = child switch
        {
            FolderNode folder => _folders.Remove(folder),
            FileNode file => _files.Remove(file),
            _ => false
        };
        if (!removed)
        {
            return false;
        }

        var files = child is FolderNode removedFolder ? removedFolder._fileCount : 1;
        var folders = child is FolderNode nested ? nested._folderCount + 1 : 0;
        for (var folder = this; folder is not null; folder = folder.Parent)
        {
            folder._size -= child.Size;
            folder._fileCount -= files;
            folder._folderCount -= folders;
        }

        return true;
    }

    /// <summary>Finds a node in this subtree by its full path (case-insensitive).</summary>
    public StorageNode? Find(string fullPath)
    {
        var target = Path.TrimEndingDirectorySeparator(fullPath.Trim().Replace('/', '\\'));
        var own = Path.TrimEndingDirectorySeparator(FullPath);
        if (string.Equals(target, own, StringComparison.OrdinalIgnoreCase))
        {
            return this;
        }

        if (!target.StartsWith(own, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // "D:\Data2" starts with "D:\Data" but is not inside it.
        var isInside = own.EndsWith('\\') || target[own.Length] == '\\';
        if (!isInside)
        {
            return null;
        }

        var segments = target[own.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var current = this;
        for (var i = 0; i < segments.Length; i++)
        {
            var isLast = i == segments.Length - 1;
            var folder = current._folders.Find(f => string.Equals(f.Name, segments[i], StringComparison.OrdinalIgnoreCase));
            if (folder is not null)
            {
                if (isLast)
                {
                    return folder;
                }

                current = folder;
                continue;
            }

            return isLast
                ? current._files.Find(f => string.Equals(f.Name, segments[i], StringComparison.OrdinalIgnoreCase))
                : null;
        }

        return null;
    }

    /// <summary>Depth-first enumeration of every folder and file below this one.</summary>
    public IEnumerable<StorageNode> Descendants()
    {
        var pending = new Stack<FolderNode>();
        pending.Push(this);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            foreach (var file in folder._files)
            {
                yield return file;
            }

            foreach (var child in folder._folders)
            {
                yield return child;
                pending.Push(child);
            }
        }
    }

    public IEnumerable<FileNode> DescendantFiles() => Descendants().OfType<FileNode>();
}
