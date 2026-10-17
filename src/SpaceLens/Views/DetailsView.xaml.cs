using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SpaceLens.Views;

public partial class DetailsView : UserControl
{
    public DetailsView() => InitializeComponent();

    private void OnMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }
}
