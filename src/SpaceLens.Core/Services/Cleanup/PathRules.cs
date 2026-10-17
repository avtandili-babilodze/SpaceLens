namespace SpaceLens.Core.Services.Cleanup;

/// <summary>Case-insensitive path comparisons that respect folder boundaries ("C:\Data2" is not inside "C:\Data").</summary>
public static class PathRules
{
    public static bool IsSame(string path, string other) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(path),
            Path.TrimEndingDirectorySeparator(other),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is strictly below <paramref name="folder"/>.</summary>
    public static bool IsInside(string path, string folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return false;
        }

        var root = Path.TrimEndingDirectorySeparator(folder);
        if (path.Length <= root.Length || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return root.EndsWith('\\') || path[root.Length] == '\\';
    }

    public static bool IsSameOrInside(string path, string folder) => IsSame(path, folder) || IsInside(path, folder);

    /// <summary>True when any folder in the path (not the final name) equals <paramref name="segment"/>.</summary>
    public static bool HasSegment(string path, string segment)
    {
        var start = 0;
        while (true)
        {
            var index = path.IndexOf(segment, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var end = index + segment.Length;
            var startsSegment = index == 0 || path[index - 1] == '\\';
            var endsSegment = end == path.Length || path[end] == '\\';
            if (startsSegment && endsSegment)
            {
                return true;
            }

            start = index + 1;
        }
    }
}
