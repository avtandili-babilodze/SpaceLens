using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
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
    private readonly WindowChrome _chrome;
    private bool _isFullScreen;

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
        _chrome = WindowChrome.GetWindowChrome(this);
        FitToScreen();
        EnterFullScreen();
        ApplyWindowState();
    }

    public ICommand FocusSearchCommand { get; }

    /// <summary>
    /// The design size doesn't fit every screen (a 1080p laptop at 150% has 1280 x 720 to work with).
    /// The window opens full screen; this is the size it restores to, centered by hand because
    /// WindowStartupLocation doesn't apply to a window that starts maximized.
    /// </summary>
    private void FitToScreen()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - ScreenMargin);
        Height = Math.Min(Height, area.Height - ScreenMargin);
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (_isFullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Maximized means full screen, over the taskbar too. Without a caption or resize frame the window
    /// maximizes to the whole monitor, and the taskbar only steps aside once WindowChrome is off too:
    /// it clips a maximized window to the work area. Nothing needs dragging or resizing in full screen.
    /// </summary>
    private void EnterFullScreen()
    {
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowChrome.SetWindowChrome(this, null);
        _isFullScreen = true;
        WindowState = WindowState.Maximized;
    }

    private void ExitFullScreen()
    {
        _isFullScreen = false;
        WindowState = WindowState.Normal;
        WindowChrome.SetWindowChrome(this, _chrome);
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Windows maximizes and restores on its own too (double-clicking the title area, Win+Up/Down,
        // dragging the window off the top edge); follow it into and out of full screen.
        if (WindowState == WindowState.Maximized && !_isFullScreen)
        {
            Dispatcher.BeginInvoke(EnterFullScreen);
        }
        else if (WindowState == WindowState.Normal && _isFullScreen)
        {
            Dispatcher.BeginInvoke(ExitFullScreen);
        }

        ApplyWindowState();
    }

    private void ApplyWindowState()
    {
        // A maximized window with a resize frame extends past the screen edge by that frame.
        var overhangs = WindowState == WindowState.Maximized && ResizeMode != ResizeMode.NoResize;
        Root.Margin = overhangs ? new Thickness(7) : new Thickness(0);
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
