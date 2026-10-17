using System.IO;

namespace SpaceLens.Tests.Support;

/// <summary>A real temporary folder on disk, deleted when the test ends.</summary>
public sealed class TestFolder : IDisposable
{
    public TestFolder()
    {
        Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "SpaceLensTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(string relative) => System.IO.Path.Join(Path, relative);

    public string WriteFile(string relative, int length, DateTime? lastWriteUtc = null)
    {
        var path = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
        if (lastWriteUtc is { } time)
        {
            File.SetLastWriteTimeUtc(path, time);
        }

        return path;
    }

    public string WriteFile(string relative, byte[] content)
    {
        var path = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still open by a failed test must not hide the real failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
