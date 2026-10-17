using System.Collections.ObjectModel;
using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels;

/// <summary>The folder hierarchy on the left of the explorer. Children are created lazily as folders expand.</summary>
public sealed class FolderTreeViewModel(NodePresenter presenter)
{
    private FolderNode? _root;

    public ObservableCollection<FolderTreeItemViewModel> Roots { get; } = [];

    public void Load(ScanResult scan)
    {
        _root = scan.Root;
        Roots.Clear();
        Roots.Add(new FolderTreeItemViewModel(scan.Root, presenter, scan.Drive) { IsExpanded = true });
    }

    public StorageNode? Find(string path) => _root?.Find(path);

    /// <summary>Expands the path down to <paramref name="folder"/> and selects it.</summary>
    public void Select(FolderNode folder)
    {
        var chain = new List<FolderNode>();
        for (var node = folder; node is not null; node = node.Parent)
        {
            chain.Insert(0, node);
        }

        var level = Roots;
        FolderTreeItemViewModel? item = null;
        foreach (var node in chain)
        {
            item = level.FirstOrDefault(i => ReferenceEquals(i.Folder, node));
            if (item is null)
            {
                return;
            }

            if (!ReferenceEquals(node, folder))
            {
                item.EnsureChildren();
                item.IsExpanded = true;
            }

            level = item.Children;
        }

        if (item is not null)
        {
            item.IsSelected = true;
        }
    }

    /// <summary>Re-reads sizes after items were removed from the scan.</summary>
    public void Refresh()
    {
        foreach (var root in Roots)
        {
            root.Refresh();
        }
    }
}
