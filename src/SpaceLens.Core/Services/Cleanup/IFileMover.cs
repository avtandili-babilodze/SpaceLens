namespace SpaceLens.Core.Services.Cleanup;

public interface IFileMover
{
    /// <summary>
    /// Moves files and folders into <paramref name="destinationFolder"/> with File Explorer's progress window,
    /// which also asks before replacing anything already there. Moves to another drive copy, then delete.
    /// </summary>
    FileMoveResult Move(IReadOnlyList<string> paths, string destinationFolder);
}
