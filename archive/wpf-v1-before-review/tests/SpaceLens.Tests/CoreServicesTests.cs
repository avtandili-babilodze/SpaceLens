using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using SpaceLens.Infrastructure;
using SpaceLens.Models;
using SpaceLens.Repositories;
using SpaceLens.Services.AI;
using SpaceLens.Services.Cleanup;
using SpaceLens.Services.Scanning;
using SpaceLens.Services.Security;
using Xunit;

namespace SpaceLens.Tests;

public sealed class TestWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    public string Content => Path.Combine(Root, "content");
    public AppPaths Paths { get; }
    public IScanHistoryRepository Repository { get; }
    public IDiskScanner Scanner { get; }
    public TestWorkspace()
    {
        Directory.CreateDirectory(Content); Paths = new(Path.Combine(Root, "appdata"));
        Repository = new SqliteScanHistoryRepository(Paths); Scanner = new DiskScanner(Repository, new CleanupRiskAnalyzer(Paths));
    }
    public async Task InitializeAsync() => await Repository.InitializeAsync();
    public string Write(string relative, int length)
    {
        string path = Path.Combine(Content, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]); return path;
    }
    public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }
}

public sealed class CoreServicesTests
{
    [Fact]
    public async Task ScanAggregatesNestedAndHiddenFilesWithoutDoubleCounting()
    {
        using var workspace = new TestWorkspace(); await workspace.InitializeAsync();
        workspace.Write("root.bin", 17); workspace.Write("A/one.txt", 100); workspace.Write("A/B/two.mp4", 300);
        var hidden = workspace.Write(".hidden", 29); File.SetAttributes(hidden, FileAttributes.Hidden);
        Directory.CreateDirectory(Path.Combine(workspace.Content, "empty"));
        var scan = await workspace.Scanner.ScanAsync(workspace.Content, null, default);
        Assert.Equal(ScanStatus.Completed, scan.Status); Assert.Equal(446, scan.Size); Assert.Equal(4, scan.Files); Assert.Equal(3, scan.Folders);
        var root = await workspace.Repository.GetRootAsync(scan.Id); Assert.NotNull(root); Assert.Equal(446, root.Size); Assert.Equal(4, root.FileCount);
        var a = await workspace.Repository.FindPathAsync(scan.Id, Path.Combine(workspace.Content, "A"));
        Assert.NotNull(a); Assert.Equal(400, a.Size); Assert.Equal(2, a.FileCount); Assert.Equal(1, a.FolderCount);
        var details = await workspace.Repository.GetDetailsAsync(root);
        Assert.Equal(446, details.Categories.Sum(c => c.Size)); Assert.Equal(300, details.LargestFiles[0].Size);
    }

    [Fact]
    public async Task PreCancelledScanIsExplicitlyPartialAndCannotBePlannedOrCompared()
    {
        using var w = new TestWorkspace(); await w.InitializeAsync(); w.Write("a.bin", 10);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var cancelled = await w.Scanner.ScanAsync(w.Content, null, cts.Token);
        Assert.Equal(ScanStatus.Cancelled, cancelled.Status); Assert.Equal(0, cancelled.Size);
        var complete = await w.Scanner.ScanAsync(w.Content, null, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CleanupPlanner(w.Repository).CreatePlanAsync(cancelled, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Repository.CompareAsync(cancelled.Id, complete.Id));
    }

    [Fact]
    public async Task CancellationDuringTraversalCommitsConsistentPartialTotals()
    {
        using var w = new TestWorkspace(); await w.InitializeAsync();
        for (int i = 0; i < 2000; i++) w.Write($"folder/f{i}.bin", 31);
        using var cts = new CancellationTokenSource();
        var progress = new ImmediateProgress<ScanProgress>(_ => cts.Cancel());
        var scan = await w.Scanner.ScanAsync(w.Content, progress, cts.Token);
        Assert.Equal(ScanStatus.Cancelled, scan.Status);
        var root = await w.Repository.GetRootAsync(scan.Id); Assert.NotNull(root); Assert.Equal(scan.Size, root.Size); Assert.Equal(scan.Files, root.FileCount);
    }

    [Fact]
    public async Task LinksAreRecordedButNotTraversed()
    {
        using var w = new TestWorkspace(); await w.InitializeAsync(); w.Write("A/data.bin", 123);
        string link = Path.Combine(w.Content, "A", "loop");
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(w.Content);
        using (var process = System.Diagnostics.Process.Start(start)!) { await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode); }
        try
        {
            var scan = await w.Scanner.ScanAsync(w.Content, null, default);
            Assert.Equal(123, scan.Size); Assert.Equal(1, scan.Skipped);
            var entry = await w.Repository.FindPathAsync(scan.Id, link); Assert.NotNull(entry); Assert.True(entry.IsReparsePoint); Assert.Equal(0, entry.Size); Assert.Equal(CleanupRisk.Protected, entry.Risk);
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task PermissionDeniedFolderProducesVisibleWarningAndNoInventedBytes()
    {
        using var w = new TestWorkspace(); await w.InitializeAsync(); w.Write("readable.bin", 37); w.Write("restricted/secret.bin", 400);
        var dir = new DirectoryInfo(Path.Combine(w.Content, "restricted"));
        var denied = dir.GetAccessControl();
        var identity = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(identity, FileSystemRights.ListDirectory, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
        denied.AddAccessRule(rule);
        dir.SetAccessControl(denied);
        try
        {
            var scan = await w.Scanner.ScanAsync(w.Content, null, default);
            Assert.Equal(ScanStatus.Completed, scan.Status); Assert.True(scan.Errors > 0); Assert.Equal(37, scan.Size);
            var entry = await w.Repository.FindPathAsync(scan.Id, dir.FullName); Assert.NotNull(entry); Assert.Contains("Permission denied", entry.Warning);
        }
        finally { denied.RemoveAccessRuleSpecific(rule); dir.SetAccessControl(denied); }
    }

    [Fact]
    public async Task SearchSortFilterAndPaginationUseRealStoredData()
    {
        using var w = new TestWorkspace(); await w.InitializeAsync();
        w.Write("a.txt", 1); w.Write("b.txt", 30); w.Write("literal%_.txt", 20); w.Write("nested/b.txt", 50);
        var scan = await w.Scanner.ScanAsync(w.Content, null, default); var root = (await w.Repository.GetRootAsync(scan.Id))!;
        var first = await w.Repository.GetChildrenAsync(new(scan.Id, root.Id, Filter: "Files", Limit: 2));
        Assert.Equal(3, first.Total); Assert.Equal(new long[] { 30, 20 }, first.Entries.Select(e => e.Size));
        var next = await w.Repository.GetChildrenAsync(new(scan.Id, root.Id, Filter: "Files", Offset: 2, Limit: 2)); Assert.Single(next.Entries); Assert.Equal(1, next.Entries[0].Size);
        var literal = await w.Repository.GetChildrenAsync(new(scan.Id, root.Id, Search: "%_")); Assert.Single(literal.Entries);
        var deep = await w.Repository.GetChildrenAsync(new(scan.Id, root.Id, Search: "b.txt", Recursive: true)); Assert.Equal(2, deep.Total);
        var alpha = await w.Repository.GetChildrenAsync(new(scan.Id, root.Id, Sort: "Name", Descending: false, Filter: "Files")); Assert.Equal("a.txt", alpha.Entries[0].Name);
    }

    [Fact]
    public async Task SnapshotsTrackGrowthShrinkNewAndRemovedFiles()
    {
        using var w = new TestWorkspace(); await w.InitializeAsync();
        w.Write("growth.bin", 10); w.Write("shrink.bin", 80); w.Write("removed.bin", 7);
        var a = await w.Scanner.ScanAsync(w.Content, null, default);
        w.Write("growth.bin", 100); w.Write("shrink.bin", 20); w.Write("new.bin", 50); File.Delete(Path.Combine(w.Content, "removed.bin"));
        var b = await w.Scanner.ScanAsync(w.Content, null, default);
        var diff = await w.Repository.CompareAsync(a.Id, b.Id);
        Assert.Equal(73, diff.Delta);
        Assert.Contains(diff.Changes, c => c.FullPath.EndsWith("growth.bin") && c.Delta == 90);
        Assert.Contains(diff.Changes, c => c.FullPath.EndsWith("shrink.bin") && c.Delta == -60);
        Assert.Contains(diff.Changes, c => c.Kind == "Newly discovered" && c.After == 50);
        Assert.Contains(diff.Changes, c => c.Kind == "No longer found" && c.Before == 7);
        Assert.Equal(73, diff.Changes.Where(c => !c.IsDirectory).Sum(c => c.Delta));
    }

    [Fact]
    public async Task DifferentRootsAndIdenticalSnapshotCannotBeCompared()
    {
        using var w = new TestWorkspace(); await w.InitializeAsync(); w.Write("A/file.txt", 3);
        var a = await w.Scanner.ScanAsync(w.Content, null, default); var b = await w.Scanner.ScanAsync(Path.Combine(w.Content, "A"), null, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Repository.CompareAsync(a.Id, b.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Repository.CompareAsync(a.Id, a.Id));
    }

    [Theory]
    [InlineData(@"C:\Windows\Temp\a.tmp", false, false, CleanupRisk.Protected)]
    [InlineData(@"C:\Program Files\Application\cache.bin", false, false, CleanupRisk.Protected)]
    [InlineData(@"C:\pagefile.sys", false, false, CleanupRisk.Protected)]
    [InlineData(@"C:\Users\Alex\AppData", true, false, CleanupRisk.ReviewRequired)]
    [InlineData(@"C:\Users\Alex\AppData\Local\cache.tmp", false, false, CleanupRisk.HighRisk)]
    [InlineData(@"C:\Users\Alex\Documents", true, false, CleanupRisk.ReviewRequired)]
    [InlineData(@"C:\Users\Alex\Downloads\setup.exe", false, false, CleanupRisk.ReviewRequired)]
    [InlineData(@"C:\data\unknown.tmp", false, false, CleanupRisk.ReviewRequired)]
    [InlineData(@"C:\data\junction", true, true, CleanupRisk.Protected)]
    public void CleanupIsConservative(string path, bool directory, bool link, CleanupRisk expected)
    {
        using var w = new TestWorkspace(); var policy = new CleanupRiskAnalyzer(w.Paths);
        Assert.Equal(expected, policy.Classify(path, directory, link).Risk);
    }

    [Fact]
    public async Task PlannerOnlyListsIndividualReviewableFilesAndReportsShortfall()
    {
        using var w = new TestWorkspace(); await w.InitializeAsync();
        w.Write("Windows/system.bin", 100); w.Write("AppData/state.db", 90); w.Write("Downloads/setup.exe", 20); w.Write("Documents/unique.txt", 10);
        var scan = await w.Scanner.ScanAsync(w.Content, null, default);
        var plan = await new CleanupPlanner(w.Repository).CreatePlanAsync(scan, 1000);
        Assert.Equal(30, plan.PotentialBytes); Assert.False(plan.MeetsGoal); Assert.Equal(2, plan.Candidates.Count);
        Assert.All(plan.Candidates, c => { Assert.False(c.IsDirectory); Assert.Equal(CleanupRisk.ReviewRequired, c.Risk); });
        Assert.Contains("does not reach", plan.Explanation);
    }

    [Theory]
    [InlineData("I need to free 25 GB", 25_000_000_000L)]
    [InlineData("1.5 GiB", 1_610_612_736L)]
    [InlineData("30", 32_212_254_720L)]
    [InlineData("20 MiB", 20_971_520L)]
    public void GoalParserUnderstandsUnits(string input, long bytes) => Assert.Equal(bytes, StorageGoalParser.Parse(input));

    [Theory]
    [InlineData("-5 GiB")]
    [InlineData("0 GiB")]
    [InlineData("no amount")]
    public void InvalidGoalsAreRejected(string goal) => Assert.Throws<FormatException>(() => StorageGoalParser.Parse(goal));

    [Fact]
    public async Task FolderPrefixDoesNotIncludeSimilarlyNamedSiblings()
    {
        using var w = new TestWorkspace(); await w.InitializeAsync();
        w.Write("a%_/one.bin", 40); w.Write("aXX/other.bin", 90);
        var scan = await w.Scanner.ScanAsync(w.Content, null, default);
        var folder = (await w.Repository.FindPathAsync(scan.Id, Path.Combine(w.Content, "a%_")))!;
        var details = await w.Repository.GetDetailsAsync(folder);
        Assert.Equal(40, details.Categories.Sum(c => c.Size)); Assert.Single(details.LargestFiles);
    }

    [Fact]
    public async Task DpapiRoundTripsWithoutPlaintextAndSupportsRemoval()
    {
        using var w = new TestWorkspace(); var store = new WindowsCredentialStore(w.Paths);
        const string testKey = "sk-ant-test-key-not-a-real-credential";
        await store.SaveAsync(testKey); Assert.True(store.HasCredential); Assert.Equal(testKey, await store.LoadAsync());
        Assert.DoesNotContain(testKey, System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(w.Paths.CredentialPath)));
        await store.RemoveAsync(); Assert.False(store.HasCredential); Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task ContextIsBoundedToRelevantMetadataAndNeverContainsFileContents()
    {
        using var w = new TestWorkspace(); await w.InitializeAsync();
        const string secret = "PRIVATE_DOCUMENT_CONTENT_NEVER_UPLOAD";
        File.WriteAllText(Path.Combine(w.Content, "personal.txt"), secret);
        for (int i = 0; i < 50; i++) w.Write($"data{i}.bin", i);
        var scan = await w.Scanner.ScanAsync(w.Content, null, default); var root = (await w.Repository.GetRootAsync(scan.Id))!;
        var context = await new AiContextBuilder(w.Repository).BuildAsync(scan, root, "What takes the most space?");
        Assert.DoesNotContain(secret, context.Json); Assert.True(context.References.Count <= 32); Assert.Contains("logical bytes", context.Json);
    }

    [Fact]
    public async Task DuplicateFinderUsesContentHashesAndSkipsChangedFiles()
    {
        using var w = new TestWorkspace(); await w.InitializeAsync();
        w.Write("one.bin", 64); w.Write("two.bin", 64);
        File.WriteAllBytes(Path.Combine(w.Content, "different.bin"), Enumerable.Repeat((byte)1, 64).ToArray());
        w.Write("changed.bin", 64);
        var scan = await w.Scanner.ScanAsync(w.Content, null, default); w.Write("changed.bin", 65);
        var result = await new DuplicateFinder(w.Repository).FindAsync(scan.Id, new ImmediateProgress<string>(_ => { }), default);
        Assert.Single(result.Groups); Assert.Equal(2, result.Groups[0].Files.Count); Assert.Equal(64, result.Groups[0].RedundantBytes); Assert.Equal(1, result.Skipped);
    }

    private sealed class ImmediateProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }

    [Fact]
    public async Task ComparisonDefaultsAdvanceToNewestScanAfterRescanning()
    {
        using var w = new TestWorkspace();
        await w.InitializeAsync();
        w.Write("file.bin", 10);
        var first = await w.Scanner.ScanAsync(w.Content, null, default);
        var vm = new SpaceLens.ViewModels.CompareViewModel(new SpaceLens.Services.Storage.StorageHistoryService(w.Repository));
        await vm.ReloadAsync();
        Assert.Equal(first.Id, vm.After!.Id);
        w.Write("file.bin", 25);
        var second = await w.Scanner.ScanAsync(w.Content, null, default);
        await vm.ReloadAsync();
        Assert.Equal(second.Id, vm.After!.Id);
        Assert.Equal(first.Id, vm.Before!.Id);
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal(15, vm.Comparison!.Delta);
    }
}
