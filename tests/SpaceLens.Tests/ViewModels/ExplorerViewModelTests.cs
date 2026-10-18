using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Services.Session;
using SpaceLens.Tests.Support;
using SpaceLens.ViewModels;

namespace SpaceLens.Tests.ViewModels;

public sealed class ExplorerViewModelTests
{
    private readonly FakeRecycleBin _bin = new();

    [Fact]
    public async Task OpensOnTheScannedRootWithTheLargestItemsFirst()
    {
        var (session, explorer) = Create();

        await session.LoadAsync(TestData.Scan(TestData.TypicalDrive()));

        Assert.Same(session.Current!.Root, explorer.CurrentFolder);
        Assert.Equal(explorer.Table.Rows.Select(r => r.Size).OrderDescending(), explorer.Table.Rows.Select(r => r.Size));
        Assert.Single(explorer.Tree.Roots);
    }

    [Fact]
    public async Task RowMenuRecyclesOnlyTheItemItWasOpenedOn()
    {
        var (session, explorer) = Create();
        await session.LoadAsync(TestData.Scan(TestData.TypicalDrive()));
        explorer.NavigateTo(session.Current!.Root.Find(@"C:\Users\alex\Downloads"));
        var archive = explorer.Table.Rows.Single(r => r.Name == "photos.zip");
        var installer = explorer.Table.Rows.Single(r => r.Name == "setup-old.exe");
        archive.IsChecked = true;

        await explorer.RecycleCommand.ExecuteAsync(installer);

        Assert.Equal([installer.FullPath], _bin.Recycled);
        Assert.DoesNotContain(explorer.Table.Rows, r => r.Name == "setup-old.exe");
    }

    [Fact]
    public async Task SelectionBarRecyclesEveryTickedRow()
    {
        var (session, explorer) = Create();
        await session.LoadAsync(TestData.Scan(TestData.TypicalDrive()));
        explorer.NavigateTo(session.Current!.Root.Find(@"C:\Users\alex\Downloads"));
        foreach (var row in explorer.Table.Rows.Where(r => r.Name is "photos.zip" or "setup-old.exe"))
        {
            row.IsChecked = true;
        }

        await explorer.RecycleCheckedCommand.ExecuteAsync(null);

        Assert.Equal(2, _bin.Recycled.Count);
    }

    private (ScanSession Session, ExplorerViewModel Explorer) Create()
    {
        var session = TestServices.Session();
        var executor = TestServices.Executor(session, _bin, new ScriptedDialogs(answer: true));
        var explorer = new ExplorerViewModel(
            session, TestServices.Presenter, new NoShell(), executor, new NoFolderPicker(), new MemorySettingsStore(), new StrongReferenceMessenger());
        return (session, explorer);
    }
}
