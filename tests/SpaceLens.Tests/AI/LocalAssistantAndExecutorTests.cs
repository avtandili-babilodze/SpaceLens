using SpaceLens.Core.Models;
using SpaceLens.Core.Services.AI;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Tests.Support;

namespace SpaceLens.Tests.AI;

public sealed class LocalAssistantAndExecutorTests
{
    [Fact]
    public async Task OfflineAssistantAnswersSpaceGoalsWithAPlan()
    {
        var session = await TestServices.LoadedSessionAsync();
        var assistant = new LocalStorageAssistant(session, TestData.RiskAnalyzer, new CleanupPlanner());

        var reply = await assistant.AskAsync("Help me free 3 GB", null, CancellationToken.None);

        Assert.NotNull(reply.Plan);
        Assert.Equal(3 * ByteSize.GB, reply.Plan!.GoalBytes);
    }

    [Fact]
    public async Task OfflineAssistantExplainsTheRiskOfTheSelectedItem()
    {
        var session = await TestServices.LoadedSessionAsync();
        var assistant = new LocalStorageAssistant(session, TestData.RiskAnalyzer, new CleanupPlanner());
        var vhdx = session.Current!.Root.Find(@"C:\Users\alex\Documents\vm\disk.vhdx");

        var reply = await assistant.AskAsync("Can I safely delete this?", vhdx, CancellationToken.None);

        Assert.Contains("High Risk", reply.Text);
        Assert.Contains("[[C:\\Users\\alex\\Documents\\vm\\disk.vhdx]]", reply.Text);
    }

    [Fact]
    public async Task OfflineAssistantSaysWhenThereIsNoHistoryToCompare()
    {
        var session = await TestServices.LoadedSessionAsync();
        var assistant = new LocalStorageAssistant(session, TestData.RiskAnalyzer, new CleanupPlanner());

        var reply = await assistant.AskAsync("Which folders are growing?", null, CancellationToken.None);

        Assert.Contains("scanned at least twice", reply.Text);
    }

    [Fact]
    public async Task ExecutorNeverRecyclesProtectedOrHighRiskItems()
    {
        var session = await TestServices.LoadedSessionAsync();
        var bin = new FakeRecycleBin();
        var dialogs = new ScriptedDialogs(answer: true);
        var executor = TestServices.Executor(session, bin, dialogs);
        var root = session.Current!.Root;
        string[] paths =
        [
            @"C:\Windows\System32\kernel32.dll",
            @"C:\Users\alex\Documents\vm\disk.vhdx",
            @"C:\Users\alex\Downloads\setup-old.exe"
        ];
        var items = paths
            .Select(path => root.Find(path)!)
            .ToList();

        var outcome = await executor.RecycleAsync(items);

        Assert.Equal([@"C:\Users\alex\Downloads\setup-old.exe"], bin.Recycled);
        Assert.Equal(1, outcome.RecycledCount);
        Assert.Contains("left out", dialogs.Requests.Single().Footnote);
        Assert.Null(root.Find(@"C:\Users\alex\Downloads\setup-old.exe"));
    }

    [Fact]
    public async Task ExecutorDoesNothingWhenTheUserCancels()
    {
        var session = await TestServices.LoadedSessionAsync();
        var bin = new FakeRecycleBin();
        var executor = TestServices.Executor(session, bin, new ScriptedDialogs(answer: false));
        var installer = session.Current!.Root.Find(@"C:\Users\alex\Downloads\setup-old.exe")!;
        var sizeBefore = session.Current.Root.Size;

        var outcome = await executor.RecycleAsync([installer]);

        Assert.True(outcome.WasCancelled);
        Assert.Empty(bin.Recycled);
        Assert.Equal(sizeBefore, session.Current.Root.Size);
    }

    [Fact]
    public async Task RecyclingUpdatesTheTreeAndTheCleanupAnalysis()
    {
        var session = await TestServices.LoadedSessionAsync();
        var executor = TestServices.Executor(session, new FakeRecycleBin(), new ScriptedDialogs(answer: true));
        var root = session.Current!.Root;
        var installer = root.Find(@"C:\Users\alex\Downloads\setup-old.exe")!;
        var sizeBefore = root.Size;

        await executor.RecycleAsync([installer]);

        Assert.Equal(sizeBefore - installer.Size, root.Size);
        Assert.DoesNotContain(session.Cleanup.Opportunities, o => o.Kind == CleanupKind.OldInstallers);
    }

    [Fact]
    public async Task ExecutorKeepsFoldersThatHoldRepositories()
    {
        var root = TestData.TypicalDrive();
        TestData.File(root, @"C:\Users\alex\Downloads\project\.git\HEAD", ByteSize.KB);
        TestData.File(root, @"C:\Users\alex\Downloads\project\readme.md", ByteSize.KB);
        var session = await TestServices.LoadedSessionAsync(root);
        var bin = new FakeRecycleBin();
        var dialogs = new ScriptedDialogs(answer: true);
        var executor = TestServices.Executor(session, bin, dialogs);
        var project = root.Find(@"C:\Users\alex\Downloads\project")!;
        var installer = root.Find(@"C:\Users\alex\Downloads\setup-old.exe")!;

        var outcome = await executor.RecycleAsync([project, installer]);

        Assert.Equal([installer.FullPath], bin.Recycled);
        Assert.Equal(1, outcome.RecycledCount);
        Assert.Contains("repositories", dialogs.Requests.Single().Footnote);
    }

    [Fact]
    public async Task ExecutorRecyclesAFolderOnceWhenItemsInsideItAreAlsoSelected()
    {
        var session = await TestServices.LoadedSessionAsync();
        var bin = new FakeRecycleBin();
        var executor = TestServices.Executor(session, bin, new ScriptedDialogs(answer: true));
        var root = session.Current!.Root;
        var cache = root.Find(@"C:\Users\alex\AppData\Local\npm-cache")!;
        var blob = root.Find(@"C:\Users\alex\AppData\Local\npm-cache\_cacache\blob")!;

        await executor.RecycleAsync([blob, cache]);

        Assert.Equal([cache.FullPath], bin.Recycled);
    }
}
