using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Tests.Support;

namespace SpaceLens.Tests.Scanning;

public sealed class DiskScannerTests
{
    private readonly DiskScanner _scanner = TestServices.Scanner();

    [Fact]
    public async Task Scan_AddsUpNestedFoldersIncludingHiddenFiles()
    {
        using var folder = new TestFolder();
        folder.WriteFile("root.bin", 17);
        folder.WriteFile(@"A\one.txt", 100);
        folder.WriteFile(@"A\B\two.mp4", 300);
        var hidden = folder.WriteFile(".hidden", 29);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        Directory.CreateDirectory(folder.Combine("Empty"));

        var result = await _scanner.ScanAsync(folder.Path, null, CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal(446, result.Root.Size);
        Assert.Equal(4, result.Root.FileCount);
        Assert.Equal(3, result.Root.FolderCount);
        var a = Assert.IsType<FolderNode>(result.Root.Find(folder.Combine("A")));
        Assert.Equal(400, a.Size);
        Assert.Equal(2, a.FileCount);
        Assert.Equal(1, a.FolderCount);
    }

    [Fact]
    public async Task Scan_RecordsJunctionsWithoutFollowingThem()
    {
        using var folder = new TestFolder();
        folder.WriteFile(@"A\data.bin", 123);
        var loop = folder.Combine(@"A\loop");
        CreateJunction(loop, folder.Path);

        try
        {
            var result = await _scanner.ScanAsync(folder.Path, null, CancellationToken.None);

            Assert.Equal(123, result.Root.Size);
            Assert.Equal(1, result.SkippedLinkCount);
            var link = Assert.IsType<FolderNode>(result.Root.Find(loop));
            Assert.True(link.IsLink);
            Assert.Equal(0, link.Size);
        }
        finally
        {
            Directory.Delete(loop);
        }
    }

    [Fact]
    public async Task Scan_MarksDeniedFoldersAndCountsNoBytesForThem()
    {
        using var folder = new TestFolder();
        folder.WriteFile("readable.bin", 37);
        folder.WriteFile(@"restricted\secret.bin", 400);
        var restricted = new DirectoryInfo(folder.Combine("restricted"));
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var security = restricted.GetAccessControl();
        security.AddAccessRule(rule);
        restricted.SetAccessControl(security);

        try
        {
            var result = await _scanner.ScanAsync(folder.Path, null, CancellationToken.None);

            Assert.True(result.IsComplete);
            Assert.Equal(37, result.Root.Size);
            Assert.Equal(1, result.UnreadableFolderCount);
            Assert.True(Assert.IsType<FolderNode>(result.Root.Find(restricted.FullName)).IsUnreadable);
        }
        finally
        {
            security.RemoveAccessRule(rule);
            restricted.SetAccessControl(security);
        }
    }

    [Fact]
    public async Task Scan_ThatIsCancelledReturnsAPartialResult()
    {
        using var folder = new TestFolder();
        folder.WriteFile(@"A\a.bin", 10);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await _scanner.ScanAsync(folder.Path, null, cancellation.Token);

        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task Scan_ReportsProgress()
    {
        using var folder = new TestFolder();
        for (var i = 0; i < 50; i++)
        {
            folder.WriteFile($@"F{i % 5}\file{i}.bin", 10);
        }

        var reports = new List<ScanProgress>();
        var result = await _scanner.ScanAsync(folder.Path, new SynchronousProgress<ScanProgress>(reports.Add), CancellationToken.None);

        Assert.NotEmpty(reports);
        Assert.Equal(50, reports[^1].Files);
        Assert.Equal(500, reports[^1].Bytes);
        Assert.Equal(50, result.Root.FileCount);
    }

    [Fact]
    public async Task Scan_OfAMissingFolderFails()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            _scanner.ScanAsync(Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N")), null, CancellationToken.None));
    }

    [Theory]
    [InlineData("C:", @"C:\")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\Users\", @"C:\Users")]
    [InlineData("\"C:\\Users\"", @"C:\Users")]
    public void NormalizePath_TurnsUserInputIntoAFullPath(string input, string expected) =>
        Assert.Equal(expected, DiskScanner.NormalizePath(input));

    private static void CreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(target);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
