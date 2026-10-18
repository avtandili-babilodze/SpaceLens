using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SpaceLens.Core.Services.Alerts;

namespace SpaceLens.Views;

/// <summary>
/// A small card in the corner of the screen with the daily check's findings. It closes itself after a while unless
/// the mouse is over it.
/// </summary>
public partial class AlertWindow : Window
{
    private static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(45);
    private const double ScreenMargin = 8;

    private readonly Action _open;
    private readonly DispatcherTimer _closeTimer = new() { Interval = ShowFor };

    public AlertWindow(IReadOnlyList<DriveAlert> alerts, Action open)
    {
        InitializeComponent();
        _open = open;
        AlertList.ItemsSource = alerts;
        _closeTimer.Tick += (_, _) =>
        {
            if (!IsMouseOver)
            {
                Close();
            }
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - ScreenMargin;
            Top = area.Bottom - ActualHeight - ScreenMargin;
            _closeTimer.Start();
        };
        Closed += (_, _) => _closeTimer.Stop();
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        _open();
        Close();
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => Close();
}
