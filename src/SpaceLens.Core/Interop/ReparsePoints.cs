namespace SpaceLens.Core.Interop;

/// <summary>
/// Tells real links (junctions, symbolic links, mount points) apart from other reparse points such as
/// OneDrive placeholders, which are ordinary folders that must be scanned.
/// </summary>
internal static class ReparsePoints
{
    // Bit 29 of a reparse tag marks a "name surrogate": the entry stands in for another location.
    private const uint NameSurrogateBit = 0x20000000;

    /// <summary>True when following the directory could visit another location (and possibly loop).</summary>
    public static bool IsLink(string directoryPath)
    {
        var tag = TryGetTag(directoryPath);

        // When the tag can't be read, err on the safe side and treat it as a link.
        return tag is null || (tag.Value & NameSurrogateBit) != 0;
    }

    private static uint? TryGetTag(string path)
    {
        var handle = NativeMethods.FindFirstFileExW(
            ToExtendedPath(path),
            NativeMethods.FindExInfoBasic,
            out var data,
            NativeMethods.FindExSearchNameMatch,
            IntPtr.Zero,
            0);
        if (handle == NativeMethods.InvalidHandle)
        {
            return null;
        }

        NativeMethods.FindClose(handle);
        return (data.FileAttributes & FileAttributes.ReparsePoint) != 0 ? data.Reserved0 : 0;
    }

    private static string ToExtendedPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }
}
