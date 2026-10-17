using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SpaceLens.ViewModels;

namespace SpaceLens.Views;

/// <summary>Window chrome and responsive layout only; everything else lives in <see cref="MainViewModel"/>.</summary>
public partial class MainWindow : Window
{
    /// <summary>Below this width the sidebar becomes an icon rail and the right column narrows.</summary>
    private const double CompactWidth = 1500;
    private const double ScreenMargin = 40;

    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        FocusSearchCommand = new RelayCommand(() =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        });
        InitializeComponent();
        DataContext = viewModel;
        FitToScreen();
    }

    public ICommand FocusSearchCommand { get; }

    /// <summary>The design size doesn't fit every screen (a 1080p laptop at 150% has 1280 x 720 to work with).</summary>
    private void FitToScreen()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - ScreenMargin);
        Height = Math.Min(Height, area.Height - ScreenMargin);
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // A maximized chromeless window extends past the screen edge by the resize border.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var isCompact = e.NewSize.Width < CompactWidth;
        SidebarColumn.Width = new GridLength(isCompact ? 76 : 220);
        RightColumn.Width = new GridLength(isCompact ? 330 : 380);
        _viewModel.IsCompact = isCompact;
    }

    private void OnClosing(object? sender, CancelEventArgs e) => _viewModel.Scan.CancelCommand.Execute(null);
}
