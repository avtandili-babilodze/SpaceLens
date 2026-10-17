using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SpaceLens.ViewModels;

namespace SpaceLens.Views;

/// <summary>Bridges tree and grid events to <see cref="ExplorerViewModel"/> commands.</summary>
public partial class ExplorerView : UserControl
{
    public ExplorerView() => InitializeComponent();

    private ExplorerViewModel? ViewModel => DataContext as ExplorerViewModel;

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderTreeItemViewModel { Folder: { } folder } && !ReferenceEquals(ViewModel?.CurrentFolder, folder))
        {
            ViewModel?.NavigateTo(folder);
        }
    }

    private void OnTreeItemSelected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item)
        {
            item.BringIntoView();
        }
    }

    private void OnTreeReveal(object sender, RoutedEventArgs e)
    {
        if (TreeItemFrom(sender) is { Folder: { } folder })
        {
            ViewModel?.NavigateTo(folder);
            ViewModel?.RevealCommand.Execute(null);
        }
    }

    private void OnTreeAsk(object sender, RoutedEventArgs e)
    {
        if (TreeItemFrom(sender) is { Folder: { } folder })
        {
            ViewModel?.NavigateTo(folder);
            ViewModel?.AskAboutCommand.Execute(null);
        }
    }

    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        // Sorting happens in the view model over the whole listing, not just the visible rows.
        e.Handled = true;
        if (ViewModel is null || string.IsNullOrEmpty(e.Column.SortMemberPath))
        {
            return;
        }

        ViewModel.Table.SortByCommand.Execute(e.Column.SortMemberPath);
        foreach (var column in ItemsGrid.Columns)
        {
            column.SortDirection = null;
        }

        e.Column.SortDirection = ViewModel.Table.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
    }

    /// <summary>On narrow windows the secondary columns give their space to the name (right-click still opens the menu).</summary>
    private void OnGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var changed = SetVisible(TypeColumn, width >= 660)
            | SetVisible(ModifiedColumn, width >= 560)
            | SetVisible(MenuColumn, width >= 470);
        if (changed)
        {
            // DataGrid keeps stale widths when columns appear or disappear mid-layout; re-apply them afterwards.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ResetColumnWidths(ItemsGrid));
        }
    }

    internal static bool SetVisible(DataGridColumn column, bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (column.Visibility == visibility)
        {
            return false;
        }

        column.Visibility = visibility;
        return true;
    }

    internal static void ResetColumnWidths(DataGrid grid)
    {
        foreach (var column in grid.Columns)
        {
            // Value is the declared width; DisplayValue may already be the squeezed one.
            var width = column.Width;
            column.Width = 0;
            column.Width = new DataGridLength(width.Value, width.UnitType);
        }
    }

    /// <summary>Long paths keep the deepest folder in view.</summary>
    private void OnBreadcrumbsSizeChanged(object sender, SizeChangedEventArgs e) => BreadcrumbScroller.ScrollToRightEnd();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: StorageItemViewModel item })
        {
            ViewModel?.OpenCommand.Execute(item);
        }
    }

    private void OnRowRightClick(object sender, MouseButtonEventArgs e)
    {
        // Right-clicking a row selects it, so the context menu acts on what the user pointed at.
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { } row)
        {
            row.IsSelected = true;
        }
    }

    private void OnRowMenu(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StorageItemViewModel item } button && ItemsGrid.ContextMenu is { } menu)
        {
            ItemsGrid.SelectedItem = item;
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;

            // A menu takes its DataContext from the placement target, which here is the row, not the explorer.
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }

    private void OnFolderMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }

    private static FolderTreeItemViewModel? TreeItemFrom(object sender) =>
        (sender as MenuItem)?.DataContext as FolderTreeItemViewModel;

    private static T? FindAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        while (element is not null and not T)
        {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return element as T;
    }
}
