using System.Runtime.InteropServices;
using SpaceLens.Core.Interop;

namespace SpaceLens.Core.Services.Cleanup;

/// <summary>Uses the Windows shell, the same mechanism as Delete in File Explorer.</summary>
public sealed class RecycleBinService : IRecycleBinService
{
    public RecycleBinInfo Query(string driveRoot)
    {
        if (!Environment.Is64BitProcess)
        {
            return default;
        }

        var info = new NativeMethods.ShQueryRecycleBinInfo { Size = Marshal.SizeOf<NativeMethods.ShQueryRecycleBinInfo>() };
        var result = NativeMethods.SHQueryRecycleBinW(Path.GetPathRoot(driveRoot) ?? driveRoot, ref info);
        return result == 0 ? new RecycleBinInfo(info.Bytes, info.ItemCount) : default;
    }

    public RecycleOutcome MoveToRecycleBin(IReadOnlyList<string> paths)
    {
        EnsureSupportedProcess();
        var existing = paths.Where(Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (existing.Count == 0)
        {
            return new RecycleOutcome([], paths.ToList(), WasCancelled: false);
        }

        var operation = new NativeMethods.ShFileOperation
        {
            Function = NativeMethods.FoDelete,

            // The shell expects a list of paths separated and terminated by NUL characters.
            From = string.Join('\0', existing) + "\0\0",
            Flags = (ushort)(NativeMethods.FofAllowUndo
                | NativeMethods.FofNoConfirmation
                | NativeMethods.FofSilent
                | NativeMethods.FofNoErrorUi
                | NativeMethods.FofWantNukeWarning)
        };

        var result = NativeMethods.SHFileOperationW(ref operation);
        var recycled = existing.Where(p => !Exists(p)).ToList();
        var failed = paths.Except(recycled, StringComparer.OrdinalIgnoreCase).ToList();
        var wasCancelled = operation.AnyOperationsAborted || (result != 0 && recycled.Count == 0);
        return new RecycleOutcome(recycled, failed, wasCancelled);
    }

    public void Empty(string driveRoot)
    {
        EnsureSupportedProcess();
        var flags = NativeMethods.SherbNoConfirmation | NativeMethods.SherbNoProgressUi | NativeMethods.SherbNoSound;
        var result = NativeMethods.SHEmptyRecycleBinW(IntPtr.Zero, Path.GetPathRoot(driveRoot) ?? driveRoot, flags);
        if (result != 0 && result != NativeMethods.RecycleBinAlreadyEmpty)
        {
            var reason = Marshal.GetExceptionForHR(result)?.Message ?? $"error 0x{result:X8}";
            throw new InvalidOperationException("Windows couldn't empty the Recycle Bin: " + reason);
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>The shell structures are declared with 64-bit packing; a 32-bit process must not use them.</summary>
    private static void EnsureSupportedProcess()
    {
        if (!Environment.Is64BitProcess)
        {
            const string Message = "Moving items to the Recycle Bin needs the 64-bit version of SpaceLens.";
            throw new PlatformNotSupportedException(Message);
        }
    }
}
