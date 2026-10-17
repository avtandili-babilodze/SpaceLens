using SpaceLens.Core.Configuration;
using SpaceLens.Core.Models;
using SpaceLens.Core.Repositories;
using SpaceLens.Core.Services.Storage;
using SpaceLens.Tests.Support;

namespace SpaceLens.Tests.Storage;

public sealed class StorageHistoryServiceTests
{
    private static async Task<StorageHistoryService> CreateAsync(TestFolder folder)
    {
        var repository = new SqliteScanHistoryRepository(new AppPaths(folder.Path));
        await repository.InitializeAsync();
        return new StorageHistoryService(repository);
    }

    private static Task<SnapshotComparison?> SaveScanAsync(StorageHistoryService history, FolderNode root) =>
        history.SaveAsync(history.CreateRecord(TestData.Scan(root)));

    private static FolderNode Drive(long videoBytes, bool withNewFile)
    {
        var root = FolderNode.CreateRoot(@"C:\", TestData.LongAgo);
        TestData.File(root, @"C:\Users\alex\Videos\movie.mp4", videoBytes);
        TestData.File(root, @"C:\Users\alex\Documents\notes.txt", 50 * ByteSize.MB);
        TestData.File(root, @"C:\Windows\big.bin", 2 * ByteSize.GB);
        if (withNewFile)
        {
            TestData.File(root, @"C:\Users\alex\Downloads\game.iso", 4 * ByteSize.GB);
        }

        return root;
    }

    [Fact]
    public async Task FirstScanHasNothingToCompareWith()
    {
        using var folder = new TestFolder();
        var history = await CreateAsync(folder);

        var growth = await SaveScanAsync(history, Drive(1 * ByteSize.GB, withNewFile: false));

        Assert.Null(growth);
        Assert.Single(await history.GetSnapshotsAsync());
    }

    [Fact]
    public async Task SecondScanShowsTheMostSpecificGrowth()
    {
        using var folder = new TestFolder();
        var history = await CreateAsync(folder);
        await SaveScanAsync(history, Drive(1 * ByteSize.GB, withNewFile: false));

        var growth = await SaveScanAsync(history, Drive(3 * ByteSize.GB, withNewFile: true));

        Assert.NotNull(growth);
        Assert.Equal(6 * ByteSize.GB, growth!.NetChange);

        // Users and C:\ grew too, but only because of these two folders, so they aren't listed.
        Assert.Equal([@"C:\Users\alex\Downloads", @"C:\Users\alex\Videos"], growth.Grew.Select(c => c.Path).Order());
        Assert.True(growth.Grew.Single(c => c.Path.EndsWith("Downloads")).IsNew);
        var newFile = Assert.Single(growth.NewLargeFiles, f => f.IsNew);
        Assert.Equal(@"C:\Users\alex\Downloads\game.iso", newFile.Path);
    }

    [Fact]
    public async Task OnlyScansOfTheSameLocationCanBeCompared()
    {
        using var folder = new TestFolder();
        var history = await CreateAsync(folder);
        await SaveScanAsync(history, Drive(1 * ByteSize.GB, withNewFile: false));
        var other = FolderNode.CreateRoot(@"D:\", TestData.LongAgo);
        TestData.File(other, @"D:\data.bin", 10 * ByteSize.MB);
        await SaveScanAsync(history, other);
        var snapshots = await history.GetSnapshotsAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => history.CompareAsync(snapshots[0].Id, snapshots[1].Id));
    }

    [Fact]
    public async Task OldSnapshotsArePruned()
    {
        using var folder = new TestFolder();
        var history = await CreateAsync(folder);

        for (var i = 0; i < 14; i++)
        {
            await SaveScanAsync(history, Drive((i + 1) * ByteSize.GB, withNewFile: false));
        }

        Assert.Equal(12, (await history.GetSnapshotsAsync()).Count);
    }

    [Fact]
    public async Task PartialScansAreNotSaved()
    {
        using var folder = new TestFolder();
        var history = await CreateAsync(folder);
        var partial = TestData.Scan(Drive(1 * ByteSize.GB, withNewFile: false));
        partial = new ScanResult
        {
            Root = partial.Root,
            StartedUtc = partial.StartedUtc,
            Duration = partial.Duration,
            IsComplete = false,
            UnreadableFolderCount = 0,
            SkippedLinkCount = 0
        };

        Assert.Throws<InvalidOperationException>(() => history.CreateRecord(partial));
    }
}
