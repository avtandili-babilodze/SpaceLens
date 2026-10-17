using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Session;
using SpaceLens.Services;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>The Storage Goal Solver: cleanup groups found in the scan and a plan to reach a space goal.</summary>
public sealed partial class CleanupViewModel : ObservableObject, IRecipient<OpenCleanupPlanMessage>
{
    private readonly IScanSession _session;
    private readonly ICleanupPlanner _planner;
    private readonly ICleanupExecutor _executor;
    private readonly IShellService _shell;
    private readonly NodePresenter _presenter;
    private readonly IMessenger _messenger;

    [ObservableProperty]
    private string _goalText = "20 GB";

    [ObservableProperty]
    private string? _goalError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan), nameof(PlanHeadline))]
    private CleanupPlan? _plan;

    [ObservableProperty]
    private OpportunityViewModel? _selected;

    [ObservableProperty]
    private bool _isWorking;

    public CleanupViewModel(
        IScanSession session,
        ICleanupPlanner planner,
        ICleanupExecutor executor,
        IShellService shell,
        NodePresenter presenter,
        IMessenger messenger)
    {
        _session = session;
        _planner = planner;
        _executor = executor;
        _shell = shell;
        _presenter = presenter;
        _messenger = messenger;
        session.PropertyChanged += OnSessionChanged;
        messenger.Register(this);
    }

    public ObservableCollection<OpportunityViewModel> Opportunities { get; } = [];

    public bool HasScan => _session.HasScan;

    public bool IsAnalyzing => _session.IsAnalyzing;

    public bool HasOpportunities => Opportunities.Count > 0;

    public bool HasPlan => Plan is not null;

    public string LowRiskText => ByteSize.Format(_session.Cleanup.LowRiskBytes);

    public string ReviewText => ByteSize.Format(_session.Cleanup.ReviewBytes);

    public string PlanHeadline => Plan is null
        ? ""
        : Plan.MeetsGoal
            ? $"Plan reaches your goal: ~{ByteSize.Format(Plan.PlannedBytes)} of {ByteSize.Format(Plan.GoalBytes)}"
            : $"Plan frees ~{ByteSize.Format(Plan.PlannedBytes)} of {ByteSize.Format(Plan.GoalBytes)}";

    public void Receive(OpenCleanupPlanMessage message)
    {
        GoalText = ByteSize.Format(message.GoalBytes);
        CreatePlan();
        _messenger.Send(new NavigateMessage(AppPage.Cleanup));
    }

    [RelayCommand]
    private void CreatePlan()
    {
        GoalError = null;
        if (!StorageGoalParser.TryParse(GoalText, out var goal))
        {
            GoalError = "Enter an amount such as 20 GB, 500 MB or 1.5 TB.";
            return;
        }

        if (!_session.HasScan)
        {
            GoalError = "Scan a drive first.";
            return;
        }

        ApplyPlan(_planner.CreatePlan(_session.Cleanup, goal));
    }

    [RelayCommand]
    private void Select(OpportunityViewModel? opportunity)
    {
        if (Selected is not null)
        {
            Selected.IsSelected = false;
        }

        Selected = opportunity;
        if (opportunity is not null)
        {
            opportunity.IsSelected = true;
        }
    }

    [RelayCommand]
    private async Task RunActionAsync(OpportunityViewModel? opportunity)
    {
        opportunity ??= Selected;
        if (opportunity is null)
        {
            return;
        }

        IsWorking = true;
        try
        {
            switch (opportunity.Action)
            {
                case CleanupAction.EmptyRecycleBin:
                    if (await _executor.EmptyRecycleBinAsync(opportunity.Opportunity.Bytes))
                    {
                        _messenger.Send(new NotificationMessage("The Recycle Bin was emptied."));
                    }

                    break;
                case CleanupAction.OpenStorageSettings:
                    _shell.OpenStorageSettings();
                    break;
                default:
                    var outcome = await _executor.RecycleAsync(opportunity.CheckedNodes);
                    if (outcome.RecycledCount > 0 || outcome.FailedCount > 0)
                    {
                        _messenger.Send(new NotificationMessage(outcome.Describe(), IsError: outcome.FailedCount > 0));
                    }

                    break;
            }
        }
        catch (InvalidOperationException exception)
        {
            _messenger.Send(new NotificationMessage(exception.Message, IsError: true));
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private void ShowInExplorer(StorageItemViewModel? item)
    {
        if (item is not null)
        {
            _messenger.Send(new ShowInExplorerMessage(item.Node));
        }
    }

    [RelayCommand]
    private void Reveal(StorageItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            _shell.RevealInExplorer(item.FullPath);
        }
        catch (System.IO.IOException exception)
        {
            _messenger.Send(new NotificationMessage(exception.Message, IsError: true));
        }
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IScanSession.Cleanup):
                Rebuild();
                break;
            case nameof(IScanSession.IsAnalyzing):
                OnPropertyChanged(nameof(IsAnalyzing));
                break;
            case nameof(IScanSession.Current):
                Plan = null;
                OnPropertyChanged(nameof(HasScan));
                break;
        }
    }

    private void Rebuild()
    {
        var selectedKind = Selected?.Kind;
        Opportunities.Clear();
        foreach (var opportunity in _session.Cleanup.Opportunities)
        {
            Opportunities.Add(new OpportunityViewModel(opportunity, _presenter));
        }

        OnPropertyChanged(nameof(HasOpportunities));
        OnPropertyChanged(nameof(LowRiskText));
        OnPropertyChanged(nameof(ReviewText));

        if (Plan is not null)
        {
            ApplyPlan(_planner.CreatePlan(_session.Cleanup, Plan.GoalBytes));
        }

        Select(Opportunities.FirstOrDefault(o => o.Kind == selectedKind) ?? Opportunities.FirstOrDefault());
    }

    private void ApplyPlan(CleanupPlan plan)
    {
        Plan = plan;
        foreach (var opportunity in Opportunities)
        {
            opportunity.PlanStep = null;
            opportunity.PlannedBytes = 0;
        }

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            var opportunity = Opportunities.FirstOrDefault(o => o.Kind == step.Opportunity.Kind);
            if (opportunity is null)
            {
                continue;
            }

            opportunity.PlanStep = i + 1;
            opportunity.PlannedBytes = step.Bytes;
            if (opportunity.HasItems)
            {
                opportunity.CheckOnly(step.Items);
            }
        }

        Select(Opportunities.FirstOrDefault(o => o.IsInPlan) ?? Selected);
    }
}
