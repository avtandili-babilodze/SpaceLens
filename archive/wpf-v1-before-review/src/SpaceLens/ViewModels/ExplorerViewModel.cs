using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceLens.Models;
using SpaceLens.Repositories;
using SpaceLens.Services.Platform;

namespace SpaceLens.ViewModels;

public sealed partial class ExplorerViewModel(IScanHistoryRepository repository, IWindowsPlatformService platform) : ObservableObject
{
    private int navigationVersion, detailsVersion, queryVersion;
    private CancellationTokenSource? searchDelay;
    private int offset;
    public StorageSnapshot? Snapshot { get; private set; }
    public ObservableCollection<FolderNodeViewModel> Roots { get; } = [];
    public ObservableCollection<FileEntry> Entries { get; } = [];
    public ObservableCollection<FileEntry> Breadcrumbs { get; } = [];
    public string[] Filters { get; } = ["All items", "Folders", "Files", "Large files (≥1 GiB)", "Review required"];
    public string[] SortKeys { get; } = ["Size", "Name", "Type", "Modified", "Risk"];
    [ObservableProperty] private FileEntry? currentFolder;
    [ObservableProperty] private FileEntry? focusedEntry;
    [ObservableProperty] private FolderDetails? details;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string filter = "All items";
    [ObservableProperty] private string sortKey = "Size";
    [ObservableProperty] private bool descending = true;
    [ObservableProperty] private bool searchSubfolders;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private long totalItems;
    [ObservableProperty] private int selectionCount;
    [ObservableProperty] private string status = "Choose a drive or folder, then start a scan.";
    [ObservableProperty] private string? error;
    public bool HasScan => Snapshot is not null;
    public string PageText => TotalItems == 0 ? "0 items" : $"{offset + 1:N0}–{Math.Min(offset + Entries.Count, TotalItems):N0} of {TotalItems:N0} items";
    public event Action<FileEntry>? ContextChanged;

    public async Task LoadSnapshotAsync(StorageSnapshot snapshot)
    {
        Snapshot = snapshot; OnPropertyChanged(nameof(HasScan));
        var root = await repository.GetRootAsync(snapshot.Id);
        Roots.Clear();
        if (root is null) { Status = "This scan contains no readable entries."; return; }
        var node = new FolderNodeViewModel(root, repository) { IsExpanded = true };
        Roots.Add(node);
        await NavigateAsync(root);
    }

    [RelayCommand]
    public async Task NavigateAsync(FileEntry? entry)
    {
        if (entry is null) return;
        if (entry.IsReparsePoint) { Error = "This link or junction was intentionally skipped. Scan its original physical location instead."; return; }
        if (!entry.IsDirectory)
        {
            if (entry.ParentId is long parentId && await repository.GetEntryAsync(parentId) is { } parent) await NavigateAsync(parent);
            FocusedEntry = entry; await InspectAsync(entry); return;
        }
        int version = ++navigationVersion;
        CurrentFolder = entry; FocusedEntry = null; offset = 0; Error = null;
        ContextChanged?.Invoke(entry);
        var crumbs = new List<FileEntry>(); FileEntry? cursor = entry;
        while (cursor is not null)
        {
            crumbs.Add(cursor); cursor = cursor.ParentId is long parent ? await repository.GetEntryAsync(parent) : null;
        }
        if (version != navigationVersion) return;
        crumbs.Reverse(); Breadcrumbs.Clear(); foreach (var crumb in crumbs) Breadcrumbs.Add(crumb);
        await RefreshAsync(); await InspectAsync(entry);
        if (version == navigationVersion) await SelectTreeAsync(crumbs);
    }
    private async Task SelectTreeAsync(IReadOnlyList<FileEntry> crumbs)
    {
        var collection = Roots;
        foreach (var crumb in crumbs)
        {
            var node = collection.FirstOrDefault(n => n.Entry?.Id == crumb.Id);
            if (node is null) return;
            node.IsExpanded = true; await node.LoadChildrenAsync();
            node.IsSelected = crumb.Id == CurrentFolder?.Id; collection = node.Children;
        }
    }
    partial void OnFocusedEntryChanged(FileEntry? value) { if (value is not null) _ = InspectAsync(value); }
    private async Task InspectAsync(FileEntry entry)
    {
        int version = ++detailsVersion;
        try { var result = await repository.GetDetailsAsync(entry); if (version == detailsVersion) { Details = result; ContextChanged?.Invoke(entry); } }
        catch (Exception ex) { Error = ex.Message; }
    }
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (Snapshot is null || CurrentFolder is null) return;
        int version = navigationVersion; int query = ++queryVersion; long parentId = CurrentFolder.Id; string search = SearchText;
        IsLoading = true;
        try
        {
            var page = await repository.GetChildrenAsync(new(Snapshot.Id, parentId, SearchText, SortKey, Descending, Filter, offset, 300, SearchSubfolders));
            if (version != navigationVersion || query != queryVersion || search != SearchText) return;
            Entries.Clear(); foreach (var entry in page.Entries) Entries.Add(entry);
            TotalItems = page.Total; OnPropertyChanged(nameof(PageText));
            Status = Snapshot.Status != ScanStatus.Completed ? "Partial scan · start a new scan for complete results" : Snapshot.Errors > 0 ? $"{Snapshot.Errors:N0} access errors · unreadable content is excluded" : $"{Snapshot.Files:N0} files measured · links and junctions skipped";
            Error = null;
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { if (query == queryVersion) IsLoading = false; }
    }
    private async Task DebouncedRefreshAsync()
    {
        searchDelay?.Cancel(); searchDelay?.Dispose(); searchDelay = new(); var token = searchDelay.Token;
        try { await Task.Delay(250, token); offset = 0; await RefreshAsync(); } catch (OperationCanceledException) { }
    }
    partial void OnSearchTextChanged(string value) => _ = DebouncedRefreshAsync();
    partial void OnFilterChanged(string value) { offset = 0; _ = RefreshAsync(); }
    partial void OnSortKeyChanged(string value) { offset = 0; _ = RefreshAsync(); }
    partial void OnDescendingChanged(bool value) { offset = 0; _ = RefreshAsync(); }
    partial void OnSearchSubfoldersChanged(bool value) { offset = 0; _ = RefreshAsync(); }
    [RelayCommand] private void ToggleSortDirection() => Descending = !Descending;
    [RelayCommand] private async Task NextPageAsync() { if (offset + Entries.Count < TotalItems) { offset += 300; await RefreshAsync(); } }
    [RelayCommand] private async Task PreviousPageAsync() { if (offset > 0) { offset = Math.Max(0, offset - 300); await RefreshAsync(); } }
    [RelayCommand] private async Task UpAsync() { if (CurrentFolder?.ParentId is long id) await NavigateAsync(await repository.GetEntryAsync(id)); }
    [RelayCommand] private void Reveal(FileEntry? entry) { try { if (entry is not null) platform.Reveal(entry.FullPath); } catch (Exception ex) { Error = ex.Message; } }
    [RelayCommand] private void CopyPath(FileEntry? entry) { try { if (entry is not null) platform.CopyPath(entry.FullPath); } catch (Exception ex) { Error = ex.Message; } }
}
