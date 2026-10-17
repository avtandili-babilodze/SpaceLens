using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels;

/// <summary>The item table: which rows are shown, in what order, and which are ticked for removal.</summary>
public sealed partial class ItemTableViewModel : ObservableObject
{
    /// <summary>Rows are capped so a folder with a huge number of files stays responsive; the largest are kept.</summary>
    private const int MaxRows = 10_000;

    private readonly NodePresenter _presenter;
    private List<StorageNode> _nodes = [];
    private DriveSummary? _drive;
    private long? _parentSize;
    private bool _isBulkChecking;

    [ObservableProperty]
    private IReadOnlyList<StorageItemViewModel> _rows = [];

    [ObservableProperty]
    private StorageItemViewModel? _selected;

    [ObservableProperty]
    private FilterOption _filter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChecked), nameof(CheckedText))]
    private int _checkedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckedText))]
    private long _checkedBytes;

    public ItemTableViewModel(NodePresenter presenter)
    {
        _presenter = presenter;
        Filters =
        [
            new(ItemFilter.All, "All items"),
            new(ItemFilter.Folders, "Folders"),
            new(ItemFilter.Files, "Files"),
            new(ItemFilter.LargeFiles, "Larger than 1 GB"),
            new(ItemFilter.Removable, "Cleanup candidates")
        ];
        _filter = Filters[0];
    }

    public IReadOnlyList<FilterOption> Filters { get; }

    public string SortColumn { get; private set; } = nameof(StorageItemViewModel.Size);

    public bool SortDescending { get; private set; } = true;

    /// <summary>Items that pass the filter, including any beyond the row cap.</summary>
    public int MatchingCount { get; private set; }

    public bool IsTruncated => MatchingCount > Rows.Count;

    public bool HasChecked => CheckedCount > 0;

    public string CheckedText => $"{CheckedCount:N0} selected  ·  {ByteSize.Format(CheckedBytes)}";

    public IReadOnlyList<StorageNode> CheckedNodes => Rows.Where(r => r.IsChecked).Select(r => r.Node).ToList();

    /// <summary>Header check box: true when every removable row is ticked; setting it ticks or clears them all.</summary>
    public bool AreAllChecked
    {
        get => Rows.Any(r => r.CanRemove) && Rows.Where(r => r.CanRemove).All(r => r.IsChecked);
        set
        {
            // Ticking thousands of rows one by one would recount the totals each time.
            _isBulkChecking = true;
            foreach (var row in Rows)
            {
                row.IsChecked = value && row.CanRemove;
            }

            _isBulkChecking = false;
            UpdateChecked();
        }
    }

    /// <summary>Raised after the rows were rebuilt (new items, filter or sort).</summary>
    public event EventHandler? RowsChanged;

    /// <summary>Shows <paramref name="nodes"/>; <paramref name="parentSize"/> drives the share-of-folder bars.</summary>
    public void Show(IEnumerable<StorageNode> nodes, DriveSummary? drive, long? parentSize, StorageNode? select = null)
    {
        _nodes = nodes.ToList();
        _drive = drive;
        _parentSize = parentSize;
        Rebuild(select);
    }

    /// <summary>Drops items that no longer exist (after a recycle) without leaving the current view.</summary>
    public void Retain(Func<StorageNode, bool> keep)
    {
        _nodes = _nodes.Where(keep).ToList();
        Rebuild(Selected?.Node);
    }

    public void Select(StorageNode? node) =>
        Selected = node is null ? null : Rows.FirstOrDefault(r => ReferenceEquals(r.Node, node));

    [RelayCommand]
    private void SortBy(string column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            // Size, date and risk read best biggest, newest or riskiest first; names read A to Z.
            SortDescending = column is nameof(StorageItemViewModel.Size)
                or nameof(StorageItemViewModel.Modified)
                or nameof(StorageItemViewModel.Risk);
        }

        Rebuild(Selected?.Node);
    }

    [RelayCommand]
    private void ClearChecks() => AreAllChecked = false;

    partial void OnFilterChanged(FilterOption value) => Rebuild(Selected?.Node);

    private void Rebuild(StorageNode? select)
    {
        var matching = _nodes.Where(MatchesFilter).ToList();
        MatchingCount = matching.Count;
        var shown = matching.Count > MaxRows ? matching.OrderByDescending(n => n.Size).Take(MaxRows).ToList() : matching;
        var rows = Sort(shown.Select(n => new StorageItemViewModel(n, _presenter, _drive, _parentSize)));

        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        foreach (var row in rows)
        {
            row.PropertyChanged += OnRowChanged;
        }

        Rows = rows;
        UpdateChecked();
        Select(select);
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<StorageItemViewModel> Sort(IEnumerable<StorageItemViewModel> rows)
    {
        // Folders stay above files when sorting by name or type, like File Explorer.
        var byText = SortColumn is nameof(StorageItemViewModel.Name) or nameof(StorageItemViewModel.TypeName);
        var ordered = byText ? rows.OrderByDescending(r => r.IsFolder) : rows.OrderBy(_ => 0);
        Func<StorageItemViewModel, object> key = SortColumn switch
        {
            nameof(StorageItemViewModel.Name) => r => r.Name,
            nameof(StorageItemViewModel.TypeName) => r => r.TypeName,
            nameof(StorageItemViewModel.Modified) => r => r.Modified,
            nameof(StorageItemViewModel.Risk) => r => r.Risk,
            _ => r => r.Size
        };

        var comparer = byText
            ? Comparer<object>.Create((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a, b))
            : Comparer<object>.Default;
        return (SortDescending ? ordered.ThenByDescending(key, comparer) : ordered.ThenBy(key, comparer)).ToList();
    }

    private bool MatchesFilter(StorageNode node) => Filter.Value switch
    {
        ItemFilter.Folders => node.IsFolder,
        ItemFilter.Files => !node.IsFolder,
        ItemFilter.LargeFiles => !node.IsFolder && node.Size >= ByteSize.GB,
        ItemFilter.Removable => _presenter.Assess(node).AllowsRemoval,
        _ => true
    };

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StorageItemViewModel.IsChecked) && !_isBulkChecking)
        {
            UpdateChecked();
        }
    }

    private void UpdateChecked()
    {
        var checkedRows = Rows.Where(r => r.IsChecked).ToList();
        CheckedCount = checkedRows.Count;
        CheckedBytes = checkedRows.Sum(r => r.Size);
        OnPropertyChanged(nameof(AreAllChecked));
    }
}
