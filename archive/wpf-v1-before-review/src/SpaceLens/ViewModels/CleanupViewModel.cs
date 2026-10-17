using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceLens.Models;
using SpaceLens.Services.Cleanup;

namespace SpaceLens.ViewModels;

public sealed partial class CleanupViewModel(ICleanupPlanner planner) : ObservableObject
{
    public StorageSnapshot? Snapshot { get; set; }
    public ObservableCollection<FileEntry> Candidates { get; } = [];
    [ObservableProperty] private string goal = "25 GiB";
    [ObservableProperty] private CleanupPlan? plan;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string status = "Choose a completed scan, then set your storage goal. Every item remains yours to review.";
    public event Func<FileEntry, Task>? InspectRequested;
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SolveAsync(CancellationToken token)
    {
        if (Snapshot is null) { Status = "Scan a drive or folder first."; return; }
        IsBusy = true;
        try
        {
            Plan = await planner.CreatePlanAsync(Snapshot, StorageGoalParser.Parse(Goal), token);
            Candidates.Clear(); foreach (var candidate in Plan.Candidates) Candidates.Add(candidate);
            Status = Plan.Explanation;
        }
        catch (OperationCanceledException) { Status = "Planning cancelled."; }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }
    public void Reset(StorageSnapshot snapshot) { Snapshot = snapshot; Plan = null; Candidates.Clear(); Status = "Set a goal to build a review list from this scan. No files will be deleted."; }
    [RelayCommand] private async Task InspectAsync(FileEntry? entry) { if (entry is not null && InspectRequested is not null) await InspectRequested(entry); }
}
