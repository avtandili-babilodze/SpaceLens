using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceLens.Models;
using SpaceLens.Services.Storage;

namespace SpaceLens.ViewModels;

public sealed partial class CompareViewModel(IStorageHistoryService history) : ObservableObject
{
    public ObservableCollection<StorageSnapshot> Snapshots { get; } = [];
    public ObservableCollection<StorageChange> Changes { get; } = [];
    [ObservableProperty] private StorageSnapshot? before;
    [ObservableProperty] private StorageSnapshot? after;
    [ObservableProperty] private SnapshotComparison? comparison;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string status = "Completed scans are saved automatically on this device. Compare two scans of the same location.";
    public event Action<SnapshotComparison>? Compared;
    public event Func<StorageChange, Task>? LocateRequested;
    public async Task ReloadAsync()
    {
        var snapshots = await history.GetHistoryAsync(); Snapshots.Clear();
        foreach (var snapshot in snapshots.Where(s => s.Status == ScanStatus.Completed)) Snapshots.Add(snapshot);
        After = Snapshots.FirstOrDefault();
        Before = Snapshots.FirstOrDefault(s => s.Id < After?.Id && string.Equals(s.RootPath, After?.RootPath, StringComparison.OrdinalIgnoreCase));
    }
    [RelayCommand]
    private async Task CompareAsync()
    {
        if (Before is null || After is null) { Status = "Choose two completed snapshots of the same location."; return; }
        IsBusy = true;
        try
        {
            Comparison = await history.CompareAsync(Before.Id, After.Id);
            Changes.Clear(); foreach (var change in Comparison.Changes) Changes.Add(change);
            Status = $"Net logical size change: {Comparison.DeltaText}. Showing up to 500 largest changes. Folder rows include descendants; do not sum nested rows. 'Newly discovered' does not establish creation time.";
            if (Before.Errors > 0 || After.Errors > 0) Status += " Access errors can affect these differences.";
            Compared?.Invoke(Comparison);
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private async Task LocateAsync(StorageChange? change) { if (change is not null && LocateRequested is not null) await LocateRequested(change); }
}
