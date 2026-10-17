using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SpaceLens.Models;
using SpaceLens.Repositories;

namespace SpaceLens.ViewModels;

public sealed partial class FolderNodeViewModel : ObservableObject
{
    private readonly IScanHistoryRepository repository;
    private Task? loadTask;
    public FileEntry? Entry { get; }
    public string Name => Entry?.Name ?? "Loading…";
    public string SizeText => Entry?.SizeText ?? "";
    public string Icon => "\uE8B7";
    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];
    [ObservableProperty] private bool isExpanded;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string? error;

    public FolderNodeViewModel(FileEntry? entry, IScanHistoryRepository repository)
    {
        Entry = entry; this.repository = repository;
        if (entry is { IsReparsePoint: false, FolderCount: > 0 }) Children.Add(new(null, repository));
    }
    partial void OnIsExpandedChanged(bool value) { if (value) _ = LoadChildrenAsync(); }
    public Task LoadChildrenAsync() => loadTask ??= LoadChildrenCoreAsync();
    private async Task LoadChildrenCoreAsync()
    {
        if (Entry is null || Entry.IsReparsePoint) return;
        try
        {
            var nodes = new List<FileEntry>(); int offset = 0;
            EntryPage page;
            do
            {
                page = await repository.GetChildrenAsync(new(Entry.SnapshotId, Entry.Id, Sort: "Name", Descending: false, Filter: "Folders", Offset: offset, Limit: 1000));
                nodes.AddRange(page.Entries); offset += page.Entries.Count;
            } while (offset < page.Total && page.Entries.Count > 0);
            Children.Clear(); foreach (var entry in nodes) Children.Add(new(entry, repository));
        }
        catch (Exception ex) { Error = ex.Message; }
    }
}
