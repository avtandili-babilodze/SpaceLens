using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceLens.Models;
using SpaceLens.Services.Scanning;

namespace SpaceLens.ViewModels;

public sealed partial class DuplicatesViewModel(IDuplicateFinder finder) : ObservableObject
{
    public StorageSnapshot? Snapshot { get; set; }
    public ObservableCollection<DuplicateGroup> Groups { get; } = [];
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string status = "Compare SHA-256 hashes of matching-size files. Files are read locally and never removed.";
    public event Func<FileEntry, Task>? InspectRequested;
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task FindAsync(CancellationToken token)
    {
        if (Snapshot?.Status != ScanStatus.Completed) { Status = "Complete a scan first."; return; }
        IsBusy = true; Status = "Finding same-size candidates…"; Groups.Clear();
        try
        {
            var result = await finder.FindAsync(Snapshot.Id, new Progress<string>(s => Status = s), token);
            foreach (var group in result.Groups) Groups.Add(group);
            Status = $"{Groups.Count:N0} identical-content groups · {ByteSize.Format(Groups.Sum(g => g.RedundantBytes))} redundant logical size. {result.Skipped:N0} changed or unreadable files skipped. Checks up to 10,000 size-matched candidates. Hard links can refer to the same physical data; savings are not guaranteed.";
        }
        catch (OperationCanceledException) { Status = "Duplicate check cancelled. No files were changed."; }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private async Task InspectAsync(FileEntry? entry) { if (entry is not null && InspectRequested is not null) await InspectRequested(entry); }
    public void Reset(StorageSnapshot snapshot) { Snapshot = snapshot; Groups.Clear(); Status = "Ready to hash matching-size files from the selected scan. No files will be removed."; }
}
