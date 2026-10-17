using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpaceLens.Models;
using SpaceLens.ViewModels;

namespace SpaceLens.Views;

public partial class ExplorerView : UserControl
{
    public ExplorerView() => InitializeComponent();
    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ExplorerViewModel vm && e.NewValue is FolderNodeViewModel { Entry: { } entry } && vm.CurrentFolder?.Id != entry.Id)
            vm.NavigateCommand.Execute(entry);
    }
    private void Files_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ExplorerViewModel vm && FileGrid.SelectedItem is FileEntry entry && IsRowSource(e.OriginalSource as DependencyObject)) vm.NavigateCommand.Execute(entry);
    }
    private static bool IsRowSource(DependencyObject? source)
    {
        while (source is not null) { if (source is DataGridRow) return true; source = System.Windows.Media.VisualTreeHelper.GetParent(source); }
        return false;
    }
    private void Files_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (DataContext is ExplorerViewModel vm) vm.SelectionCount = FileGrid.SelectedItems.Count; }
    private void Files_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not ExplorerViewModel vm) return;
        string key = e.Column.SortMemberPath;
        if (vm.SortKey == key) vm.Descending = !vm.Descending; else vm.SortKey = key;
        e.Column.SortDirection = vm.Descending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending;
    }
}
