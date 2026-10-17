using SpaceLens.Core.Models;
using SpaceLens.Tests.Support;
using SpaceLens.ViewModels;

namespace SpaceLens.Tests.ViewModels;

public sealed class OpportunityViewModelTests
{
    private const int ItemCount = OpportunityViewModel.MaxRows + 500;

    [Fact]
    public void SelectionCoversItemsBeyondTheDisplayedRows()
    {
        var viewModel = new OpportunityViewModel(LargeTempGroup(), TestServices.Presenter);

        viewModel.CheckAllCommand.Execute(null);

        Assert.Equal(OpportunityViewModel.MaxRows, viewModel.Items.Count);
        Assert.Equal(ItemCount, viewModel.CheckedNodes.Count);
        Assert.Equal(ItemCount, viewModel.CheckedCount);
    }

    [Fact]
    public void UntickingARowRemovesOnlyThatItem()
    {
        var viewModel = new OpportunityViewModel(LargeTempGroup(), TestServices.Presenter);
        viewModel.CheckAllCommand.Execute(null);

        viewModel.Items[0].IsChecked = false;

        Assert.Equal(ItemCount - 1, viewModel.CheckedCount);
        Assert.DoesNotContain(viewModel.Items[0].Node, viewModel.CheckedNodes);
    }

    [Fact]
    public void CheckOnlySelectsExactlyThePlannedItems()
    {
        var opportunity = LargeTempGroup();
        var viewModel = new OpportunityViewModel(opportunity, TestServices.Presenter);
        var planned = opportunity.Items.Skip(OpportunityViewModel.MaxRows).ToList();

        viewModel.CheckOnly(planned);

        Assert.Equal(planned, viewModel.CheckedNodes);
        Assert.All(viewModel.Items, row => Assert.False(row.IsChecked));
    }

    private static CleanupOpportunity LargeTempGroup()
    {
        var root = FolderNode.CreateRoot(@"C:\", TestData.LongAgo);
        var temp = TestData.Folder(root, @"C:\Users\alex\AppData\Local\Temp");
        var items = Enumerable.Range(0, ItemCount)
            .Select(i => (StorageNode)temp.AddFile($"f{i}.tmp", ByteSize.MB, TestData.LongAgo, System.IO.FileAttributes.Normal))
            .ToList();
        root.Aggregate();
        return new CleanupOpportunity
        {
            Kind = CleanupKind.TempFiles,
            Title = "Temporary files",
            Description = "Left behind by apps.",
            Risk = CleanupRisk.Low,
            Items = items
        };
    }
}
