using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels;

/// <summary>A folder in the hierarchy. Children are created only when the node is first expanded.</summary>
public sealed partial class FolderTreeItemViewModel : ObservableObject
{
    private readonly NodePresenter? _presenter;
    private bool _childrenLoaded;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public FolderTreeItemViewModel(FolderNode folder, NodePresenter presenter, DriveSummary? drive = null)
    {
        Folder = folder;
        _presenter = presenter;
        Name = NodePresenter.GetDisplayName(folder, drive);
        Icon = presenter.GetIcon(folder);
        if (HasSubfolders)
        {
            Children.Add(new FolderTreeItemViewModel());
        }
    }

    /// <summary>Placeholder child that makes the expand arrow appear before children are loaded.</summary>
    private FolderTreeItemViewModel()
    {
        Name = "";
    }

    public FolderNode? Folder { get; }

    public string Name { get; }

    public NodeIcon Icon { get; }

    public string SizeText => Folder switch
    {
        null => "",
        { IsUnreadable: true, Size: 0 } => "No access",
        _ => ByteSize.Format(Folder.Size)
    };

    public bool IsUnreadable => Folder?.IsUnreadable == true;

    public ObservableCollection<FolderTreeItemViewModel> Children { get; } = [];

    private bool HasSubfolders => Folder?.Folders.Any(f => !f.IsLink) == true;

    /// <summary>Loads children now (used when navigating deep into the tree from elsewhere).</summary>
    public void EnsureChildren()
    {
        if (_childrenLoaded || Folder is null || _presenter is null)
        {
            return;
        }

        _childrenLoaded = true;
        Children.Clear();
        foreach (var child in Folder.Folders.Where(f => !f.IsLink).OrderByDescending(f => f.Size))
        {
            Children.Add(new FolderTreeItemViewModel(child, _presenter));
        }
    }

    /// <summary>Re-reads sizes and drops children that were removed from the scan.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(SizeText));
        if (!_childrenLoaded || Folder is null)
        {
            return;
        }

        foreach (var child in Children.Where(c => c.Folder is not null && !Folder.Folders.Contains(c.Folder)).ToList())
        {
            Children.Remove(child);
        }

        foreach (var child in Children)
        {
            child.Refresh();
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            EnsureChildren();
        }
    }
}
