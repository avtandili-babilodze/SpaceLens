using SpaceLens.Core.Interop;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>Uses the Windows shell, the same mechanism as dragging items to another drive in File Explorer.</summary>
public sealed class ShellFileMover : IFileMover
{
    public FileMoveResult Move(IReadOnlyList<string> paths, string destinationFolder)
    {
        if (!Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException("Moving items needs the 64-bit version of SpaceLens.");
        }

        var existing = paths.Where(Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (existing.Count == 0)
        {
            return new FileMoveResult([], paths.ToList(), WasCancelled: false);
        }

        Directory.CreateDirectory(destinationFolder);
        var operation = new NativeMethods.ShFileOperation
        {
            Function = NativeMethods.FoMove,

            // The shell expects lists of paths separated and terminated by NUL characters.
            From = string.Join('\0', existing) + "\0\0",
            To = destinationFolder + "\0\0",

            // Progress and "replace or skip?" questions stay visible: the user should see a long copy and decide conflicts.
            Flags = (ushort)(NativeMethods.FofAllowUndo | NativeMethods.FofNoConfirmMkdir)
        };

        var result = NativeMethods.SHFileOperationW(ref operation);
        var moved = existing.Where(p => !Exists(p)).ToList();
        var failed = paths.Except(moved, StringComparer.OrdinalIgnoreCase).ToList();
        var wasCancelled = operation.AnyOperationsAborted || (result != 0 && moved.Count == 0);
        return new FileMoveResult(moved, failed, wasCancelled);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
