using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceLens.Infrastructure;
using SpaceLens.Models;
using SpaceLens.Repositories;
using SpaceLens.Services.Platform;
using SpaceLens.Services.Scanning;

namespace SpaceLens.ViewModels;

public sealed record NavigationItem(string Key, string Label, string Icon);

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDiskScanner scanner;
    private readonly IScanHistoryRepository repository;
    private readonly IWindowsPlatformService platform;
    private bool syncingDrive;
    public ExplorerViewModel Explorer { get; }
    public AssistantViewModel Assistant { get; }
    public SettingsViewModel Settings { get; }
    public CleanupViewModel Cleanup { get; }
    public CompareViewModel Compare { get; }
    public DuplicatesViewModel Duplicates { get; }
    public ObservableCollection<DriveEntry> Drives { get; } = [];
    public IReadOnlyList<NavigationItem> Navigation { get; } = [new("Overview", "Overview", "\uE80F"), new("Folders", "Folders", "\uE8B7"), new("Cleanup", "Cleanup", "\uEA99"), new("Compare", "Compare Scans", "\uE9D9"), new("Duplicates", "Duplicates", "\uE8C8"), new("Settings", "Settings", "\uE713")];
    [ObservableProperty] private NavigationItem? selectedNavigation;
    [ObservableProperty] private string page = "Overview";
    [ObservableProperty] private DriveEntry? selectedDrive;
    [ObservableProperty] private string scanPath = "";
    [ObservableProperty] private StorageSnapshot? snapshot;
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private string status = "Ready · select a drive or folder to begin";
    [ObservableProperty] private string scanProgressText = "";
    [ObservableProperty] private string? error;
    [ObservableProperty] private long reviewBytes;
    [ObservableProperty] private long largeFileCount;
    public string ReviewText => ByteSize.Format(ReviewBytes);
    public string UsedText => SelectedDrive is null ? "—" : ByteSize.Format(SelectedDrive.Used);
    public string FreeText => SelectedDrive is null ? "—" : ByteSize.Format(SelectedDrive.Available);
    public string TotalText => SelectedDrive?.Description ?? "Choose a drive";
    public double UsedPercent => SelectedDrive?.UsedPercent ?? 0;
    public string ScanLabel => Snapshot is null ? "No scan yet · your files stay on this device" : $"{Snapshot.RootPath} · {Snapshot.CreatedUtc.ToLocalTime():MMM d, HH:mm} · {Snapshot.StatusText}";
    public bool CanScan => !IsScanning && !string.IsNullOrWhiteSpace(ScanPath);
    public bool IsWorkspace => Page is "Overview" or "Folders";

    public MainViewModel(IDiskScanner scanner, IScanHistoryRepository repository, IWindowsPlatformService platform, ExplorerViewModel explorer, AssistantViewModel assistant, SettingsViewModel settings, CleanupViewModel cleanup, CompareViewModel compare, DuplicatesViewModel duplicates)
    {
        this.scanner = scanner; this.repository = repository; this.platform = platform;
        Explorer = explorer; Assistant = assistant; Settings = settings; Cleanup = cleanup; Compare = compare; Duplicates = duplicates;
        SelectedNavigation = Navigation[0];
        Explorer.ContextChanged += entry => { Assistant.CurrentFolder = entry; Assistant.ContextLabel = entry.Name; };
        Assistant.ReferenceRequested += LocateReferenceAsync;
        Cleanup.InspectRequested += InspectAsync;
        Duplicates.InspectRequested += InspectAsync;
        Compare.Compared += comparison => Assistant.Comparison = comparison;
        Compare.LocateRequested += async change =>
        {
            if (Compare.Comparison is null) return;
            long id = change.Kind == "No longer found" ? Compare.Comparison.Before.Id : Compare.Comparison.After.Id;
            var snap = await repository.GetSnapshotAsync(id);
            if (snap is null) return;
            if (Snapshot?.Id != id) await LoadSnapshotAsync(snap);
            var entry = await repository.FindPathAsync(id, change.FullPath);
            if (entry is not null) await InspectAsync(entry);
        };
    }
    public async Task InitializeAsync()
    {
        try
        {
            await repository.InitializeAsync(); await RefreshDrivesAsync(); await Compare.ReloadAsync();
            var recent = (await repository.GetSnapshotsAsync()).FirstOrDefault(s => s.Status == ScanStatus.Completed);
            if (recent is not null) await LoadSnapshotAsync(recent);
        }
        catch (Exception ex) { Error = "Startup: " + ex.Message; }
    }
    public async Task LoadSnapshotAsync(StorageSnapshot value)
    {
        ScanPath = value.RootPath;
        Snapshot = value; Assistant.Snapshot = value; Cleanup.Reset(value); Duplicates.Reset(value);
        await Explorer.LoadSnapshotAsync(value);
        if (await repository.GetRootAsync(value.Id) is { } root)
        {
            var details = await repository.GetDetailsAsync(root); ReviewBytes = details.ReviewBytes; LargeFileCount = details.LargeFileCount;
        }
        OnPropertyChanged(nameof(ScanLabel));
    }
    [RelayCommand]
    private async Task RefreshDrivesAsync()
    {
        string? current = SelectedDrive?.Path;
        string previousTarget = ScanPath;
        var drives = await platform.GetDrivesAsync(); Drives.Clear(); foreach (var drive in drives) Drives.Add(drive);
        SelectedDrive = Drives.FirstOrDefault(d => d.Path == current) ?? Drives.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(previousTarget)) ScanPath = previousTarget;
    }
    partial void OnSelectedDriveChanged(DriveEntry? value)
    {
        if (value is not null && !syncingDrive) ScanPath = value.Path;
        OnPropertyChanged(nameof(UsedText)); OnPropertyChanged(nameof(FreeText)); OnPropertyChanged(nameof(TotalText)); OnPropertyChanged(nameof(UsedPercent));
    }
    partial void OnScanPathChanged(string value)
    {
        if (Path.IsPathFullyQualified(value))
        {
            var matching = Drives.FirstOrDefault(d => string.Equals(d.Path, Path.GetPathRoot(value), StringComparison.OrdinalIgnoreCase));
            if (matching is not null && matching != SelectedDrive) { syncingDrive = true; SelectedDrive = matching; syncingDrive = false; }
        }
        OnPropertyChanged(nameof(CanScan)); ScanCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsScanningChanged(bool value) { OnPropertyChanged(nameof(CanScan)); ScanCommand.NotifyCanExecuteChanged(); }
    partial void OnReviewBytesChanged(long value) => OnPropertyChanged(nameof(ReviewText));
    partial void OnSelectedNavigationChanged(NavigationItem? value) { if (value is not null) { Page = value.Key; OnPropertyChanged(nameof(IsWorkspace)); } }
    [RelayCommand]
    private void PickFolder()
    {
        try { string? path = platform.PickFolder(); if (path is not null) ScanPath = path; }
        catch (Exception ex) { Error = ex.Message; }
    }
    [RelayCommand(CanExecute = nameof(CanScan), IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken token)
    {
        if (!CanScan) return;
        IsScanning = true; Error = null; Status = "Scanning · logical file sizes";
        var progress = new Progress<ScanProgress>(p => ScanProgressText = $"{p.Files:N0} files · {p.Folders:N0} folders · {ByteSize.Format(p.Bytes)} · {p.Errors:N0} access errors\n{p.CurrentPath}");
        try
        {
            var result = await scanner.ScanAsync(ScanPath, progress, token);
            await LoadSnapshotAsync(result); await Compare.ReloadAsync();
            Status = result.Status == ScanStatus.Completed ? $"Scan saved · {result.Files:N0} files · {result.Errors:N0} access errors · {result.Skipped:N0} links skipped" : "Scan cancelled · showing partial results";
        }
        catch (Exception ex) { Error = ex.Message; Status = "Scan failed · no success assumed"; }
        finally { IsScanning = false; await RefreshDrivesAsync(); }
    }
    public async Task ScanLocationAsync(string path) { ScanPath = path; await ScanCommand.ExecuteAsync(null); }
    private async Task InspectAsync(FileEntry entry)
    {
        if (entry.SnapshotId != Snapshot?.Id && await repository.GetSnapshotAsync(entry.SnapshotId) is { } snapshot) await LoadSnapshotAsync(snapshot);
        SelectedNavigation = Navigation.First(n => n.Key == "Folders");
        await Explorer.NavigateAsync(entry);
    }
    private async Task LocateReferenceAsync(long id) { if (await repository.GetEntryAsync(id) is { } entry) await InspectAsync(entry); }
    [RelayCommand] private async Task OpenSnapshotAsync(StorageSnapshot? value) { if (value is not null) { await LoadSnapshotAsync(value); SelectedNavigation = Navigation.First(n => n.Key == "Folders"); } }
}
