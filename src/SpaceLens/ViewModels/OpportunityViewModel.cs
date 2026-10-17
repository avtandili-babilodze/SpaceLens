using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels;

/// <summary>
/// A cleanup group on the Cleanup page. The set of checked items covers the whole group, even when only the
/// largest <see cref="MaxRows"/> are shown as rows, so a plan's selection is never silently cut short.
/// </summary>
public sealed partial class OpportunityViewModel : ObservableObject
{
    public const int MaxRows = 2_000;

    private readonly NodePresenter _presenter;
    private readonly HashSet<StorageNode> _checked = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<StorageItemViewModel>? _rows;
    private bool _isSyncingRows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInPlan), nameof(PlannedText))]
    private int? _planStep;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlannedText))]
    private long _plannedBytes;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckedText), nameof(HasChecked))]
    private int _checkedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckedText))]
    private long _checkedBytes;

    public OpportunityViewModel(CleanupOpportunity opportunity, NodePresenter presenter)
    {
        Opportunity = opportunity;
        _presenter = presenter;
    }

    public CleanupOpportunity Opportunity { get; }

    public CleanupKind Kind => Opportunity.Kind;

    public string Title => Opportunity.Title;

    public string Description => Opportunity.Description;

    public CleanupRisk Risk => Opportunity.Risk;

    public string RiskLabel => Risk == CleanupRisk.Review ? "Review" : Risk.ToLabel();

    public CleanupAction Action => Opportunity.Action;

    public string SizeText => ByteSize.Format(Opportunity.Bytes);

    public string ItemCountText => Opportunity.Action switch
    {
        CleanupAction.RecycleItems => $"{Opportunity.ItemCount:N0} item{(Opportunity.ItemCount == 1 ? "" : "s")}",
        CleanupAction.EmptyRecycleBin => "Already deleted",
        _ => "Cleaned by Windows"
    };

    public string ActionText => Opportunity.Action switch
    {
        CleanupAction.EmptyRecycleBin => "Empty Recycle Bin…",
        CleanupAction.OpenStorageSettings => "Open Storage settings",
        _ => "Move selected to Recycle Bin…"
    };

    public bool IsInPlan => PlanStep is not null;

    public string PlannedText
    {
        get
        {
            if (PlanStep is null)
            {
                return "";
            }

            var planned = "~" + ByteSize.Format(PlannedBytes);
            return PlannedBytes < Opportunity.Bytes ? $"{planned} of {SizeText}" : planned;
        }
    }

    public bool HasItems => Opportunity.Action == CleanupAction.RecycleItems;

    public bool HasChecked => CheckedCount > 0;

    public string CheckedText => $"{CheckedCount:N0} selected  ·  {ByteSize.Format(CheckedBytes)}";

    /// <summary>The largest items as rows. Built on first use because groups can hold thousands of files.</summary>
    public IReadOnlyList<StorageItemViewModel> Items => _rows ??= CreateRows();

    /// <summary>Every checked item in the group, including those beyond the displayed rows.</summary>
    public IReadOnlyList<StorageNode> CheckedNodes => Opportunity.Items.Where(_checked.Contains).ToList();

    /// <summary>Checks exactly the items a plan step chose (all items for low-risk groups).</summary>
    public void CheckOnly(IEnumerable<StorageNode> nodes)
    {
        _checked.Clear();
        _checked.UnionWith(nodes);
        SyncRows();
    }

    [RelayCommand]
    private void CheckAll()
    {
        _checked.Clear();
        _checked.UnionWith(Opportunity.Items.Where(item => _presenter.Assess(item).AllowsRemoval));
        SyncRows();
    }

    [RelayCommand]
    private void CheckNone()
    {
        _checked.Clear();
        SyncRows();
    }

    private List<StorageItemViewModel> CreateRows()
    {
        var rows = Opportunity.Items
            .Take(MaxRows)
            .Select(node => new StorageItemViewModel(node, _presenter, parentSize: Opportunity.Bytes)
            {
                IsChecked = _checked.Contains(node)
            })
            .ToList();
        foreach (var row in rows)
        {
            row.PropertyChanged += OnRowChanged;
        }

        return rows;
    }

    /// <summary>Pushes the checked set into the visible rows and recounts the totals.</summary>
    private void SyncRows()
    {
        if (_rows is not null)
        {
            _isSyncingRows = true;
            foreach (var row in _rows)
            {
                row.IsChecked = _checked.Contains(row.Node);
            }

            _isSyncingRows = false;
        }

        UpdateTotals();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSyncingRows || e.PropertyName != nameof(StorageItemViewModel.IsChecked) || sender is not StorageItemViewModel row)
        {
            return;
        }

        if (row.IsChecked)
        {
            _checked.Add(row.Node);
        }
        else
        {
            _checked.Remove(row.Node);
        }

        UpdateTotals();
    }

    private void UpdateTotals()
    {
        CheckedCount = _checked.Count;
        CheckedBytes = _checked.Sum(node => node.Size);
    }
}
