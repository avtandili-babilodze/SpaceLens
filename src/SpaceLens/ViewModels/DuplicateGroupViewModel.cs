using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels;

/// <summary>One group of identical files. The oldest copy is kept by default and the rest are pre-selected.</summary>
public sealed class DuplicateGroupViewModel
{
    public DuplicateGroupViewModel(DuplicateGroup group, NodePresenter presenter)
    {
        Group = group;
        Files = group.Files
            .OrderBy(f => f.LastWriteUtc)
            .Select(f => new StorageItemViewModel(f, presenter, parentSize: group.FileSize * group.Files.Count))
            .ToList();
        foreach (var copy in Files.Skip(1))
        {
            copy.IsChecked = copy.CanRemove;
        }
    }

    public DuplicateGroup Group { get; }

    public IReadOnlyList<StorageItemViewModel> Files { get; }

    public string Title => Files[0].Name;

    public string Summary =>
        $"{Files.Count} copies of {ByteSize.Format(Group.FileSize)}  ·  {ByteSize.Format(Group.RedundantBytes)} redundant";

    /// <summary>True when every copy is selected, which would remove the file entirely.</summary>
    public bool WouldRemoveEveryCopy => Files.All(f => f.IsChecked);

    public IEnumerable<StorageNode> SelectedCopies => Files.Where(f => f.IsChecked).Select(f => f.Node);
}
