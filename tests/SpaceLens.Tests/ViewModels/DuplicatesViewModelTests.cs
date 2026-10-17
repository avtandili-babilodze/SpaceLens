using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Duplicates;
using SpaceLens.Core.Services.Session;
using SpaceLens.Tests.Support;
using SpaceLens.ViewModels;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.Tests.ViewModels;

public sealed class DuplicatesViewModelTests
{
    private readonly FakeRecycleBin _bin = new();
    private readonly List<NotificationMessage> _notifications = [];

    [Fact]
    public async Task KeepsTheOldestCopyByDefault()
    {
        var (_, older, newer) = await LoadTwoCopiesAsync();

        var group = new DuplicateGroupViewModel(new DuplicateGroup("hash", older.Size, [newer, older]), TestServices.Presenter);

        Assert.Equal([newer], group.SelectedCopies);
        Assert.False(group.WouldRemoveEveryCopy);
    }

    [Fact]
    public async Task NeverRecyclesEveryCopyOfAFile()
    {
        var (session, older, newer) = await LoadTwoCopiesAsync();
        var viewModel = CreateViewModel(session);
        var group = new DuplicateGroupViewModel(new DuplicateGroup("hash", older.Size, [older, newer]), TestServices.Presenter);
        viewModel.Groups.Add(group);
        foreach (var copy in group.Files)
        {
            copy.IsChecked = true;
        }

        await viewModel.RecycleSelectedCommand.ExecuteAsync(null);

        Assert.Empty(_bin.Recycled);
        Assert.Contains(_notifications, n => n.IsError && n.Text.Contains("every copy"));
    }

    [Fact]
    public async Task RecyclesTheExtraCopies()
    {
        var (session, older, newer) = await LoadTwoCopiesAsync();
        var viewModel = CreateViewModel(session);
        viewModel.Groups.Add(new DuplicateGroupViewModel(new DuplicateGroup("hash", older.Size, [older, newer]), TestServices.Presenter));

        await viewModel.RecycleSelectedCommand.ExecuteAsync(null);

        Assert.Equal([newer.FullPath], _bin.Recycled);
        Assert.Empty(viewModel.Groups);
    }

    private static async Task<(ScanSession Session, FileNode Older, FileNode Newer)> LoadTwoCopiesAsync()
    {
        var root = FolderNode.CreateRoot(@"C:\", TestData.LongAgo);
        var older = TestData.File(root, @"C:\Users\alex\Videos\clip.mp4", 50 * ByteSize.MB, TestData.LongAgo);
        var newer = TestData.File(root, @"C:\Users\alex\Downloads\clip.mp4", 50 * ByteSize.MB, TestData.Yesterday);
        var session = await TestServices.LoadedSessionAsync(root);
        return (session, older, newer);
    }

    private DuplicatesViewModel CreateViewModel(IScanSession session)
    {
        var messenger = new StrongReferenceMessenger();
        messenger.Register<NotificationMessage>(this, (_, message) => _notifications.Add(message));
        var executor = TestServices.Executor(session, _bin, new ScriptedDialogs(answer: true));
        return new DuplicatesViewModel(new DuplicateFinder(), session, executor, TestData.RiskAnalyzer, TestServices.Presenter, messenger);
    }
}
