using System.Text.Json;
using SpaceLens.Models;
using SpaceLens.Repositories;

namespace SpaceLens.Services.AI;

public sealed class AiContextBuilder(IScanHistoryRepository repository)
{
    public async Task<AiContext> BuildAsync(StorageSnapshot snapshot, FileEntry selected, string question, SnapshotComparison? comparison = null)
    {
        if (snapshot.Status != ScanStatus.Completed) throw new InvalidOperationException("Complete a scan before sharing storage metadata with Claude.");
        var details = await repository.GetDetailsAsync(selected);
        var root = await repository.GetRootAsync(snapshot.Id) ?? throw new InvalidOperationException("The scan has no root folder.");
        var top = await repository.GetChildrenAsync(new(snapshot.Id, root.Id, Sort: "Size", Limit: 10));
        string term = question.Contains("AppData", StringComparison.OrdinalIgnoreCase) ? "AppData" : question.Contains("cache", StringComparison.OrdinalIgnoreCase) ? "cache" : "";
        var relevant = await repository.GetChildrenAsync(new(snapshot.Id, root.Id, Search: term, Filter: term.Length > 0 ? "Folders" : "Files", Limit: 16, Recursive: true));
        var entries = top.Entries.Concat(details.LargestFiles).Concat(relevant.Entries).Append(selected).DistinctBy(e => e.Id).Take(32).ToArray();
        var json = JsonSerializer.Serialize(new
        {
            snapshot = new { snapshot.RootPath, snapshot.CreatedUtc, snapshot.Status, snapshot.Size, snapshot.Errors, snapshot.Skipped, units = "logical bytes; hard links counted per path; sparse/compressed allocation and filesystem overhead are not measured" },
            selectedFolder = new { selected.FullPath, selected.Size, selected.FileCount, selected.FolderCount },
            categories = details.Categories.Select(c => new { c.Name, c.Size, c.Count }),
            entries = entries.Select(e => new { e.Id, e.FullPath, e.IsDirectory, e.Size, e.ModifiedUtc, risk = e.RiskText, e.Reason, e.Warning }),
            differences = comparison?.Changes.Take(12).Select(c => new { c.FullPath, c.Before, c.After, c.Delta, c.Kind }),
            notes = "This is a bounded metadata sample, not a complete inventory. Newly discovered does not prove a file was recently created. Folder sizes are recursive; do not add ancestors and descendants together. No application attribution is known."
        });
        return new(json, entries.Select(e => new AiReference(e.Id, e.Name, e.FullPath)).ToArray());
    }
}
