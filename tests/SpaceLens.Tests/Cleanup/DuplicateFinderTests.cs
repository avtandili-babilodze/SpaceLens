using System.Runtime.InteropServices;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Duplicates;
using SpaceLens.Tests.Support;

namespace SpaceLens.Tests.Cleanup;

public sealed class DuplicateFinderTests
{
    private const int Size = 2 * 1024 * 1024;

    [Fact]
    public async Task GroupsIdenticalFilesButNotSameSizeFilesWithDifferentContent()
    {
        using var folder = new TestFolder();
        var content = Pattern(Size, seed: 1);
        folder.WriteFile(@"a\copy1.bin", content);
        folder.WriteFile(@"b\copy2.bin", content);
        folder.WriteFile(@"c\copy3.bin", content);

        // Same size and same first and last 64 KB, but one byte differs in the middle.
        var almost = (byte[])content.Clone();
        almost[Size / 2] ^= 0xFF;
        folder.WriteFile(@"d\almost.bin", almost);
        folder.WriteFile(@"e\other.bin", Pattern(Size, seed: 2));

        var result = await FindAsync(folder);

        var group = Assert.Single(result.Groups);
        Assert.Equal(["copy1.bin", "copy2.bin", "copy3.bin"], group.Files.Select(f => f.Name).Order());
        Assert.Equal(2L * Size, group.RedundantBytes);
    }

    [Fact]
    public async Task FindsTypicalPhotoCopiesUnderOneMegabyteButSkipsThumbnails()
    {
        using var folder = new TestFolder();
        var photo = Pattern(880 * 1024, seed: 3);
        folder.WriteFile(@"Camera\IMG_0001.jpg", photo);
        folder.WriteFile(@"Backup\2026\holiday-beach.jpg", photo);
        var thumbnail = Pattern(40 * 1024, seed: 4);
        folder.WriteFile(@"Small\thumb.jpg", thumbnail);
        folder.WriteFile(@"Small\thumb-copy.jpg", thumbnail);

        var result = await FindAsync(folder);

        var group = Assert.Single(result.Groups);
        Assert.Equal(["IMG_0001.jpg", "holiday-beach.jpg"], group.Files.Select(f => f.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task HardLinksAreNotReportedAsDuplicates()
    {
        using var folder = new TestFolder();
        var original = folder.WriteFile(@"a\original.bin", Pattern(Size, seed: 3));
        Assert.True(CreateHardLink(folder.Combine("link.bin"), original, IntPtr.Zero));

        var result = await FindAsync(folder);

        Assert.Empty(result.Groups);
    }

    private static async Task<DuplicateSearchResult> FindAsync(TestFolder folder)
    {
        var scan = await TestServices.Scanner().ScanAsync(folder.Path, null, CancellationToken.None);
        return await new DuplicateFinder().FindAsync(scan.Root.DescendantFiles().ToList(), null, CancellationToken.None);
    }

    private static byte[] Pattern(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);
}
