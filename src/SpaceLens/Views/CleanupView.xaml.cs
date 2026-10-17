using System.Windows;
using System.Windows.Controls;
using SpaceLens.ViewModels;

namespace SpaceLens.Views;

public partial class CleanupView : UserControl
{
    public CleanupView() => InitializeComponent();

    private void OnOpportunitySelected(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is CleanupViewModel viewModel && e.AddedItems.Count > 0 && e.AddedItems[0] is OpportunityViewModel opportunity)
        {
            viewModel.SelectCommand.Execute(opportunity);
        }
    }

    /// <summary>On narrow windows the date column gives its space to the item name.</summary>
    private void OnGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ExplorerView.SetVisible(ModifiedColumn, e.NewSize.Width >= 560) && sender is DataGrid grid)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => ExplorerView.ResetColumnWidths(grid));
        }
    }
}
