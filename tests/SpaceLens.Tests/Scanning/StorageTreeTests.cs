using System.Globalization;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Tests.Support;

namespace SpaceLens.Tests.Scanning;

public sealed class StorageTreeTests
{
    [Fact]
    public void Find_IsCaseInsensitiveAndRespectsFolderBoundaries()
    {
        var root = FolderNode.CreateRoot(@"D:\Data", TestData.LongAgo);
        var file = TestData.File(root, @"D:\Data\Photos\beach.jpg", 10);

        Assert.Same(file, root.Find(@"d:\data\PHOTOS\Beach.JPG"));
        Assert.Same(root, root.Find(@"D:\Data\"));
        Assert.Null(root.Find(@"D:\Data2\Photos"));
        Assert.Null(root.Find(@"D:\Data\Missing"));
    }

    [Fact]
    public void Remove_UpdatesEveryAncestorsTotals()
    {
        var root = FolderNode.CreateRoot(@"C:\", TestData.LongAgo);
        TestData.File(root, @"C:\a\b\keep.bin", 100);
        var removed = TestData.File(root, @"C:\a\b\big.bin", 900);
        TestData.File(root, @"C:\a\c\other.bin", 50);
        root.Aggregate();

        Assert.True(removed.Parent!.Remove(removed));

        Assert.Equal(150, root.Size);
        Assert.Equal(2, root.FileCount);
        Assert.Equal(100, removed.Parent.Size);
    }

    [Fact]
    public void CloudOnlyFilesCountAsZeroBytes()
    {
        var root = FolderNode.CreateRoot(@"C:\", TestData.LongAgo);
        var placeholder = TestData.File(root, @"C:\OneDrive\movie.mp4", 5 * ByteSize.GB, attributes: (System.IO.FileAttributes)0x00400000);
        root.Aggregate();

        Assert.True(placeholder.IsCloudOnly);
        Assert.Equal(0, root.Size);
        Assert.Equal(5 * ByteSize.GB, placeholder.Length);
    }

    [Fact]
    public void LargestFiles_ReturnsTheBiggestFirst()
    {
        var root = FolderNode.CreateRoot(@"C:\", TestData.LongAgo);
        for (var i = 1; i <= 20; i++)
        {
            TestData.File(root, $@"C:\f{i % 4}\file{i}.bin", i * 10);
        }

        var largest = StorageAnalyzer.LargestFiles(root, 3);

        Assert.Equal([200L, 190L, 180L], largest.Select(f => f.Size));
    }

    [Fact]
    public void Search_KeepsTheLargestMatchesAndCountsAll()
    {
        var root = FolderNode.CreateRoot(@"C:\", TestData.LongAgo);
        for (var i = 1; i <= 30; i++)
        {
            TestData.File(root, $@"C:\cache\item{i}.cache", i);
        }

        TestData.File(root, @"C:\other\notes.txt", 1000);
        root.Aggregate();

        var result = StorageAnalyzer.Search(root, "CACHE", 5);

        Assert.Equal(31, result.TotalMatches);
        Assert.True(result.IsTruncated);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal("cache", result.Items[0].Name);
    }

    [Fact]
    public void ChildBreakdown_GroupsTheRestAsOther()
    {
        var root = FolderNode.CreateRoot(@"C:\", TestData.LongAgo);
        TestData.File(root, @"C:\a\x.bin", 500);
        TestData.File(root, @"C:\b\x.bin", 300);
        TestData.File(root, @"C:\c\x.bin", 100);
        TestData.File(root, @"C:\d\x.bin", 60);
        TestData.File(root, @"C:\e\x.bin", 40);
        root.Aggregate();

        var segments = StorageAnalyzer.ChildBreakdown(root, top: 3);

        Assert.Equal(["a", "b", "c", "Other"], segments.Select(s => s.Label));
        Assert.Equal(100, segments[^1].Bytes);
        Assert.Equal(1.0, segments.Sum(s => s.Fraction), precision: 6);
    }

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(1023, "1023 bytes")]
    [InlineData(1024, "1 KB")]
    [InlineData(5_798_205_849, "5.4 GB")]
    [InlineData(13_314_398_618, "12.4 GB")]
    [InlineData(408_021_893_120, "380 GB")]
    [InlineData(-2_147_483_648, "-2 GB")]
    public void ByteSize_FormatsLikeFileExplorer(long bytes, string expected) =>
        Assert.Equal(expected, ByteSize.Format(bytes, CultureInfo.InvariantCulture));
}
