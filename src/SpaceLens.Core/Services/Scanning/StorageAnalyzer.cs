using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Scanning;

/// <summary>Read-only queries over a scanned tree. Every method walks the tree once.</summary>
public static class StorageAnalyzer
{
    public const string OtherColor = "#5A6470";
    public const string UnaccountedColor = "#3A414A";

    // The largest children in descending shades of the accent, so the biggest reads first.
    private static readonly string[] ChildColors = ["#3D8BFF", "#6FA6FF", "#A3C4F7"];

    /// <summary>The <paramref name="count"/> largest files under <paramref name="root"/>, largest first.</summary>
    public static List<FileNode> LargestFiles(FolderNode root, int count, long minBytes = 1)
    {
        var smallest = new PriorityQueue<FileNode, long>(count + 1);
        foreach (var file in root.DescendantFiles())
        {
            if (file.Size < minBytes)
            {
                continue;
            }

            smallest.Enqueue(file, file.Size);
            if (smallest.Count > count)
            {
                smallest.Dequeue();
            }
        }

        var result = new List<FileNode>(smallest.Count);
        while (smallest.Count > 0)
        {
            result.Add(smallest.Dequeue());
        }

        result.Reverse();
        return result;
    }

    public static int CountFilesAtLeast(FolderNode root, long bytes) => root.DescendantFiles().Count(f => f.Size >= bytes);

    /// <summary>Largest direct children of a folder, with everything else grouped as "Other".</summary>
    public static List<BreakdownSegment> ChildBreakdown(FolderNode folder, int top = 3, long unaccountedBytes = 0)
    {
        var total = folder.Size + unaccountedBytes;
        var segments = new List<BreakdownSegment>();
        if (total <= 0)
        {
            return segments;
        }

        var largest = folder.Children.Where(c => c.Size > 0).OrderByDescending(c => c.Size).Take(top).ToList();
        for (var i = 0; i < largest.Count; i++)
        {
            var child = largest[i];
            var color = ChildColors[i % ChildColors.Length];
            segments.Add(new BreakdownSegment(child.Name, child.Size, (double)child.Size / total, color, child));
        }

        var other = folder.Size - largest.Sum(c => c.Size);
        if (other > 0)
        {
            segments.Add(new BreakdownSegment("Other", other, (double)other / total, OtherColor));
        }

        if (unaccountedBytes > 0)
        {
            segments.Add(new BreakdownSegment("System & unreadable", unaccountedBytes, (double)unaccountedBytes / total, UnaccountedColor));
        }

        return segments;
    }

    /// <summary>Breakdown of every file below a folder by category (videos, pictures, ...).</summary>
    public static List<BreakdownSegment> TypeBreakdown(FolderNode folder)
    {
        var totals = new Dictionary<FileCategory, long>();
        foreach (var file in folder.DescendantFiles())
        {
            var category = FileTypeClassifier.GetCategory(FileTypeClassifier.GetKind(file.Extension));
            totals[category] = totals.GetValueOrDefault(category) + file.Size;
        }

        var total = Math.Max(1, folder.Size);
        return totals
            .Where(t => t.Value > 0)
            .OrderByDescending(t => t.Value)
            .Select(t => new BreakdownSegment(
                FileTypeClassifier.GetCategoryLabel(t.Key),
                t.Value,
                (double)t.Value / total,
                FileTypeClassifier.GetCategoryColor(t.Key)))
            .ToList();
    }

    /// <summary>Items whose name contains <paramref name="query"/>, keeping only the largest <paramref name="limit"/> matches.</summary>
    public static SearchResult Search(FolderNode root, string query, int limit, CancellationToken cancellationToken = default)
    {
        var term = query.Trim();
        if (term.Length == 0)
        {
            return new SearchResult([], 0);
        }

        var smallest = new PriorityQueue<StorageNode, long>(limit + 1);
        var matches = 0;
        var visited = 0;
        foreach (var node in root.Descendants())
        {
            // Checked per node visited, not per match, so a rare term on a huge drive still stops promptly.
            if (++visited % 4096 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!node.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matches++;
            smallest.Enqueue(node, node.Size);
            if (smallest.Count > limit)
            {
                smallest.Dequeue();
            }
        }

        var items = new List<StorageNode>(smallest.Count);
        while (smallest.Count > 0)
        {
            items.Add(smallest.Dequeue());
        }

        items.Reverse();
        return new SearchResult(items, matches);
    }
}
